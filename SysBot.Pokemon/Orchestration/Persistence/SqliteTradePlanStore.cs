using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace SysBot.Pokemon;

/// <summary>
/// Synchronous SQLite persistence for durable trade-control state.
/// Each write method owns a short transaction and appends its audit event
/// before committing.
/// </summary>
public sealed class SqliteTradePlanStore : ITradePlanStore
{
    private const int CurrentSchemaVersion = 1;

    private const string MigrationV1 = """
        CREATE TABLE trade_plans (
            plan_id TEXT PRIMARY KEY,
            owner_id TEXT NOT NULL,
            game_mode TEXT NOT NULL,
            state TEXT NOT NULL CHECK (state IN (
                'draft', 'validated', 'queued', 'running', 'paused',
                'needs_attention', 'completed', 'failed', 'cancelled'
            )),
            access_json TEXT NOT NULL CHECK (json_valid(access_json)),
            evolution_policy TEXT NOT NULL,
            partner_disconnect_max_attempts INTEGER NOT NULL CHECK (partner_disconnect_max_attempts >= 0),
            transport_reconnect_delays_json TEXT NOT NULL CHECK (json_valid(transport_reconnect_delays_json)),
            retry_exhausted_policy TEXT NOT NULL,
            uncertain_settlement_policy TEXT NOT NULL,
            validation_runtime_generation TEXT NULL,
            created_at_ms INTEGER NOT NULL,
            updated_at_ms INTEGER NOT NULL,
            version INTEGER NOT NULL DEFAULT 0 CHECK (version >= 0)
        );

        CREATE TABLE trade_plan_items (
            item_id TEXT PRIMARY KEY,
            plan_id TEXT NOT NULL REFERENCES trade_plans(plan_id) ON DELETE CASCADE,
            client_item_id TEXT NOT NULL,
            position INTEGER NOT NULL CHECK (position >= 0),
            showdown_set TEXT NOT NULL CHECK (length(showdown_set) > 0),
            state TEXT NOT NULL CHECK (state IN (
                'pending', 'prepared', 'searching', 'partner_found', 'offered',
                'confirming', 'settling', 'needs_attention', 'completed',
                'skipped', 'failed'
            )),
            prepared_hash TEXT NULL,
            attempt_count INTEGER NOT NULL DEFAULT 0 CHECK (attempt_count >= 0),
            last_error_json TEXT NULL CHECK (last_error_json IS NULL OR json_valid(last_error_json)),
            settlement_evidence_json TEXT NULL CHECK (settlement_evidence_json IS NULL OR json_valid(settlement_evidence_json)),
            created_at_ms INTEGER NOT NULL,
            updated_at_ms INTEGER NOT NULL,
            version INTEGER NOT NULL DEFAULT 0 CHECK (version >= 0),
            UNIQUE (plan_id, position),
            UNIQUE (plan_id, client_item_id)
        );

        CREATE TABLE trade_operations (
            operation_id TEXT PRIMARY KEY,
            plan_id TEXT NOT NULL UNIQUE REFERENCES trade_plans(plan_id) ON DELETE CASCADE,
            state TEXT NOT NULL CHECK (state IN (
                'queued', 'running', 'paused', 'needs_attention',
                'completed', 'failed', 'cancelled'
            )),
            current_item_id TEXT NULL REFERENCES trade_plan_items(item_id),
            created_at_ms INTEGER NOT NULL,
            updated_at_ms INTEGER NOT NULL,
            version INTEGER NOT NULL DEFAULT 0 CHECK (version >= 0)
        );

        CREATE TABLE trade_events (
            event_id INTEGER PRIMARY KEY AUTOINCREMENT,
            sequence INTEGER NOT NULL CHECK (sequence > 0),
            operation_id TEXT NULL REFERENCES trade_operations(operation_id) ON DELETE CASCADE,
            plan_id TEXT NOT NULL REFERENCES trade_plans(plan_id) ON DELETE CASCADE,
            item_id TEXT NULL REFERENCES trade_plan_items(item_id) ON DELETE CASCADE,
            event_type TEXT NOT NULL CHECK (length(event_type) BETWEEN 3 AND 80),
            details_json TEXT NOT NULL CHECK (json_valid(details_json)),
            occurred_at_ms INTEGER NOT NULL,
            UNIQUE (plan_id, sequence)
        );

        CREATE TABLE trade_attempts (
            attempt_id TEXT PRIMARY KEY,
            operation_id TEXT NOT NULL REFERENCES trade_operations(operation_id) ON DELETE CASCADE,
            item_id TEXT NOT NULL REFERENCES trade_plan_items(item_id) ON DELETE CASCADE,
            attempt_number INTEGER NOT NULL CHECK (attempt_number > 0),
            started_at_ms INTEGER NOT NULL,
            ended_at_ms INTEGER NULL,
            failure_code TEXT NULL,
            irreversible_boundary_crossed INTEGER NOT NULL DEFAULT 0 CHECK (irreversible_boundary_crossed IN (0, 1)),
            UNIQUE (item_id, attempt_number)
        );

        CREATE TABLE trade_idempotency (
            scope TEXT NOT NULL,
            idempotency_key TEXT NOT NULL,
            request_hash TEXT NOT NULL,
            resource_type TEXT NOT NULL,
            resource_id TEXT NOT NULL,
            created_at_ms INTEGER NOT NULL,
            PRIMARY KEY (scope, idempotency_key)
        );

        CREATE TABLE trade_leases (
            bot_instance_id TEXT PRIMARY KEY,
            operation_id TEXT NOT NULL REFERENCES trade_operations(operation_id) ON DELETE CASCADE,
            owner_token_hash TEXT NOT NULL,
            acquired_at_ms INTEGER NOT NULL,
            expires_at_ms INTEGER NOT NULL,
            revision INTEGER NOT NULL DEFAULT 1 CHECK (revision > 0),
            CHECK (expires_at_ms > acquired_at_ms)
        );

        CREATE INDEX ix_trade_items_plan_state
            ON trade_plan_items(plan_id, state, position);
        CREATE INDEX ix_trade_operations_state
            ON trade_operations(state, updated_at_ms);
        CREATE INDEX ix_trade_events_operation_sequence
            ON trade_events(operation_id, sequence);
        CREATE INDEX ix_trade_attempts_operation_item
            ON trade_attempts(operation_id, item_id, attempt_number);
        CREATE INDEX ix_trade_leases_expiry
            ON trade_leases(expires_at_ms);
        """;

    private readonly string _databasePath;
    private readonly string _connectionString;

    public SqliteTradePlanStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            ForeignKeys = true,
            Pooling = true,
            DefaultTimeout = 5,
        }.ToString();
    }

    public string DatabasePath => _databasePath;

    public void Initialize()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var connection = OpenConnection();
        ExecuteScalar(connection, null, "PRAGMA journal_mode = WAL;");
        ExecuteNonQuery(connection, null, "PRAGMA synchronous = FULL;");

        using var transaction = connection.BeginTransaction();
        ExecuteNonQuery(connection, transaction, """
            CREATE TABLE IF NOT EXISTS trade_schema_migrations (
                version INTEGER PRIMARY KEY,
                applied_at_ms INTEGER NOT NULL
            );
            """);

        var version = Convert.ToInt32(ExecuteScalar(
            connection,
            transaction,
            "SELECT COALESCE(MAX(version), 0) FROM trade_schema_migrations;"));

        if (version > CurrentSchemaVersion)
            throw new InvalidOperationException(
                $"Trade database schema {version} is newer than supported schema {CurrentSchemaVersion}.");

        if (version < 1)
        {
            ExecuteNonQuery(connection, transaction, MigrationV1);
            ExecuteNonQuery(
                connection,
                transaction,
                "INSERT INTO trade_schema_migrations(version, applied_at_ms) VALUES (1, $applied);",
                ("$applied", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));
        }

        ExecuteNonQuery(
            connection,
            transaction,
            """
            DELETE FROM trade_leases
            WHERE expires_at_ms <= $now
               OR EXISTS (
                    SELECT 1
                    FROM trade_operations
                    WHERE trade_operations.operation_id = trade_leases.operation_id
                      AND trade_operations.state IN ('completed', 'failed', 'cancelled')
               );
            """,
            ("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()));

        transaction.Commit();
    }

    public int GetSchemaVersion()
    {
        using var connection = OpenConnection();
        return Convert.ToInt32(ExecuteScalar(
            connection,
            null,
            "SELECT COALESCE(MAX(version), 0) FROM trade_schema_migrations;"));
    }

    public TradeStoreIdempotencyResult<TradePlanSnapshot> CreatePlan(
        TradePlanDraft draft,
        string idempotencyScope,
        string idempotencyKey,
        string requestHash)
    {
        ValidateDraft(draft);
        ValidateIdempotency(idempotencyScope, idempotencyKey, requestHash);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existing = FindIdempotency(connection, transaction, idempotencyScope, idempotencyKey);
        if (existing is not null)
        {
            EnsureIdempotencyMatch(existing, requestHash, "trade_plan");
            var replayed = ReadPlan(connection, transaction, existing.ResourceId)
                ?? throw new TradeStoreNotFoundException(
                    $"Idempotency record points to missing trade plan '{existing.ResourceId}'.");
            transaction.Commit();
            return new(TradeStoreIdempotencyOutcome.Replayed, replayed);
        }

        var timestamp = draft.CreatedAt.ToUnixTimeMilliseconds();
        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO trade_plans (
                plan_id, owner_id, game_mode, state, access_json,
                evolution_policy, partner_disconnect_max_attempts,
                transport_reconnect_delays_json, retry_exhausted_policy,
                uncertain_settlement_policy, validation_runtime_generation,
                created_at_ms, updated_at_ms, version
            )
            VALUES (
                $plan_id, $owner_id, $game_mode, 'draft', $access_json,
                $evolution_policy, $partner_attempts, $reconnect_delays,
                $retry_exhausted, $uncertain_settlement, NULL,
                $created, $updated, 0
            );
            """,
            ("$plan_id", draft.PlanId),
            ("$owner_id", draft.OwnerId),
            ("$game_mode", ToStoreValue(draft.GameMode)),
            ("$access_json", draft.AccessJson),
            ("$evolution_policy", ToStoreValue(draft.Policies.Evolution)),
            ("$partner_attempts", draft.Policies.PartnerDisconnectMaxAttempts),
            ("$reconnect_delays", JsonSerializer.Serialize(draft.Policies.TransportReconnectDelaysMs)),
            ("$retry_exhausted", ToStoreValue(draft.Policies.OnRetryExhausted)),
            ("$uncertain_settlement", ToStoreValue(draft.Policies.OnUncertainSettlement)),
            ("$created", timestamp),
            ("$updated", timestamp));

        foreach (var item in draft.Items.OrderBy(item => item.Position))
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                INSERT INTO trade_plan_items (
                    item_id, plan_id, client_item_id, position, showdown_set,
                    state, prepared_hash, attempt_count, last_error_json,
                    settlement_evidence_json, created_at_ms, updated_at_ms, version
                )
                VALUES (
                    $item_id, $plan_id, $client_item_id, $position, $showdown_set,
                    'pending', NULL, 0, NULL, NULL, $created, $updated, 0
                );
                """,
                ("$item_id", item.ItemId),
                ("$plan_id", draft.PlanId),
                ("$client_item_id", item.ClientItemId),
                ("$position", item.Position),
                ("$showdown_set", item.ShowdownSet),
                ("$created", timestamp),
                ("$updated", timestamp));
        }

        AppendEvent(
            connection,
            transaction,
            draft.PlanId,
            null,
            null,
            "plan_created",
            "{}",
            draft.CreatedAt);

        InsertIdempotency(
            connection,
            transaction,
            idempotencyScope,
            idempotencyKey,
            requestHash,
            "trade_plan",
            draft.PlanId,
            draft.CreatedAt);

        var created = ReadPlan(connection, transaction, draft.PlanId)
            ?? throw new TradeStoreNotFoundException($"Created trade plan '{draft.PlanId}' was not found.");
        transaction.Commit();
        return new(TradeStoreIdempotencyOutcome.Created, created);
    }

    public TradePlanSnapshot? GetPlan(string planId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        using var connection = OpenConnection();
        return ReadPlan(connection, null, planId);
    }

    public TradePlanSnapshot TransitionPlan(
        string planId,
        TradePlanState expectedState,
        TradePlanState nextState,
        string eventType,
        string detailsJson,
        DateTimeOffset occurredAt,
        string? validationRuntimeGeneration = null,
        string? operationId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ValidateTransition(expectedState, nextState);
        ValidateEvent(eventType, detailsJson);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        if (nextState is TradePlanState.Validated)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(validationRuntimeGeneration);
            var unpreparedItems = Convert.ToInt32(ExecuteScalar(
                connection,
                transaction,
                """
                SELECT COUNT(*)
                FROM trade_plan_items
                WHERE plan_id = $plan_id AND state <> 'prepared';
                """,
                ("$plan_id", planId)));
            if (unpreparedItems != 0)
                throw new TradeStoreConflictException(
                    $"Trade plan '{planId}' cannot be validated while {unpreparedItems} item(s) are unprepared.");
        }

        var changed = ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE trade_plans
            SET state = $next_state,
                validation_runtime_generation = COALESCE($runtime_generation, validation_runtime_generation),
                updated_at_ms = $updated,
                version = version + 1
            WHERE plan_id = $plan_id AND state = $expected_state;
            """,
            ("$next_state", ToStoreValue(nextState)),
            ("$runtime_generation", validationRuntimeGeneration),
            ("$updated", occurredAt.ToUnixTimeMilliseconds()),
            ("$plan_id", planId),
            ("$expected_state", ToStoreValue(expectedState)));

        if (changed != 1)
            throw new TradeStoreConcurrencyException(
                $"Trade plan '{planId}' is missing or is no longer in state '{expectedState}'.");

        AppendEvent(
            connection,
            transaction,
            planId,
            operationId,
            null,
            eventType,
            detailsJson,
            occurredAt);

        var updated = ReadPlan(connection, transaction, planId)
            ?? throw new TradeStoreNotFoundException($"Trade plan '{planId}' was not found after transition.");
        transaction.Commit();
        return updated;
    }

    public TradePlanItemSnapshot PrepareItem(
        string planId,
        string itemId,
        string preparedHash,
        DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(preparedHash);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var changed = ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE trade_plan_items
            SET state = 'prepared',
                prepared_hash = $prepared_hash,
                updated_at_ms = $updated,
                version = version + 1
            WHERE item_id = $item_id
              AND plan_id = $plan_id
              AND state = 'pending'
              AND EXISTS (
                  SELECT 1
                  FROM trade_plans
                  WHERE plan_id = $plan_id AND state = 'draft'
              );
            """,
            ("$prepared_hash", preparedHash),
            ("$updated", occurredAt.ToUnixTimeMilliseconds()),
            ("$item_id", itemId),
            ("$plan_id", planId));

        if (changed != 1)
            throw new TradeStoreConcurrencyException(
                $"Trade item '{itemId}' is missing or is no longer pending.");

        ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE trade_plans
            SET updated_at_ms = $updated, version = version + 1
            WHERE plan_id = $plan_id;
            """,
            ("$updated", occurredAt.ToUnixTimeMilliseconds()),
            ("$plan_id", planId));

        AppendEvent(
            connection,
            transaction,
            planId,
            null,
            itemId,
            "item_prepared",
            JsonSerializer.Serialize(new { prepared_hash = preparedHash }),
            occurredAt);

        var updated = ReadItem(connection, transaction, itemId)
            ?? throw new TradeStoreNotFoundException($"Trade item '{itemId}' was not found after preparation.");
        transaction.Commit();
        return updated;
    }

    public TradeStoreIdempotencyResult<TradeOperationSnapshot> CreateOperation(
        string operationId,
        string planId,
        string idempotencyScope,
        string idempotencyKey,
        string requestHash,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ValidateIdempotency(idempotencyScope, idempotencyKey, requestHash);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existing = FindIdempotency(connection, transaction, idempotencyScope, idempotencyKey);
        if (existing is not null)
        {
            EnsureIdempotencyMatch(existing, requestHash, "trade_operation");
            var replayed = ReadOperation(connection, transaction, existing.ResourceId)
                ?? throw new TradeStoreNotFoundException(
                    $"Idempotency record points to missing trade operation '{existing.ResourceId}'.");
            transaction.Commit();
            return new(TradeStoreIdempotencyOutcome.Replayed, replayed);
        }

        var timestamp = createdAt.ToUnixTimeMilliseconds();
        var planChanged = ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE trade_plans
            SET state = 'queued', updated_at_ms = $updated, version = version + 1
            WHERE plan_id = $plan_id AND state = 'validated';
            """,
            ("$updated", timestamp),
            ("$plan_id", planId));

        if (planChanged != 1)
            throw new TradeStoreConcurrencyException(
                $"Trade plan '{planId}' is missing or is no longer validated.");

        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO trade_operations (
                operation_id, plan_id, state, current_item_id,
                created_at_ms, updated_at_ms, version
            )
            VALUES ($operation_id, $plan_id, 'queued', NULL, $created, $updated, 0);
            """,
            ("$operation_id", operationId),
            ("$plan_id", planId),
            ("$created", timestamp),
            ("$updated", timestamp));

        AppendEvent(
            connection,
            transaction,
            planId,
            operationId,
            null,
            "plan_enqueued",
            "{}",
            createdAt);

        InsertIdempotency(
            connection,
            transaction,
            idempotencyScope,
            idempotencyKey,
            requestHash,
            "trade_operation",
            operationId,
            createdAt);

        var created = ReadOperation(connection, transaction, operationId)
            ?? throw new TradeStoreNotFoundException(
                $"Created trade operation '{operationId}' was not found.");
        transaction.Commit();
        return new(TradeStoreIdempotencyOutcome.Created, created);
    }

    public TradeOperationSnapshot? GetOperation(string operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        using var connection = OpenConnection();
        return ReadOperation(connection, null, operationId);
    }

    public TradeStoreIdempotencyOutcome ClaimOperationCommand(
        string operationId,
        string idempotencyScope,
        string idempotencyKey,
        string requestHash,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ValidateIdempotency(idempotencyScope, idempotencyKey, requestHash);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        _ = ReadOperation(connection, transaction, operationId)
            ?? throw new TradeStoreNotFoundException(
                $"Trade operation '{operationId}' was not found.");

        var existing = FindIdempotency(
            connection,
            transaction,
            idempotencyScope,
            idempotencyKey);
        if (existing is not null)
        {
            EnsureIdempotencyMatch(
                existing,
                requestHash,
                "operation_command");
            if (!string.Equals(
                existing.ResourceId,
                operationId,
                StringComparison.Ordinal))
            {
                throw new TradeStoreConflictException(
                    "The idempotency key is already associated with a different operation.");
            }
            transaction.Commit();
            return TradeStoreIdempotencyOutcome.Replayed;
        }

        InsertIdempotency(
            connection,
            transaction,
            idempotencyScope,
            idempotencyKey,
            requestHash,
            "operation_command",
            operationId,
            createdAt);
        transaction.Commit();
        return TradeStoreIdempotencyOutcome.Created;
    }

    public IReadOnlyList<TradeOperationSnapshot> ListRecoverableOperations()
    {
        using var connection = OpenConnection();
        using var command = CreateCommand(
            connection,
            null,
            """
            SELECT operation_id, plan_id, state, current_item_id,
                   created_at_ms, updated_at_ms, version
            FROM trade_operations
            WHERE state NOT IN ('completed', 'failed', 'cancelled')
            ORDER BY created_at_ms, operation_id;
            """);
        using var reader = command.ExecuteReader();
        var operations = new List<TradeOperationSnapshot>();
        while (reader.Read())
            operations.Add(ReadOperation(reader));
        return operations;
    }

    public TradeOperationSnapshot TransitionOperation(
        string operationId,
        TradeOperationState expectedOperationState,
        TradeOperationState nextOperationState,
        TradePlanState expectedPlanState,
        TradePlanState nextPlanState,
        string eventType,
        string detailsJson,
        DateTimeOffset occurredAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ValidateTransition(expectedOperationState, nextOperationState);
        ValidateTransition(expectedPlanState, nextPlanState);
        ValidateEvent(eventType, detailsJson);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var planId = Convert.ToString(ExecuteScalar(
            connection,
            transaction,
            "SELECT plan_id FROM trade_operations WHERE operation_id = $operation_id;",
            ("$operation_id", operationId)));

        if (string.IsNullOrWhiteSpace(planId))
            throw new TradeStoreNotFoundException($"Trade operation '{operationId}' was not found.");

        if (nextOperationState is TradeOperationState.NeedsAttention)
        {
            var attentionItems = Convert.ToInt32(ExecuteScalar(
                connection,
                transaction,
                """
                SELECT COUNT(*)
                FROM trade_plan_items
                WHERE plan_id = $plan_id AND state = 'needs_attention';
                """,
                ("$plan_id", planId)));
            if (attentionItems == 0)
                throw new TradeStoreConflictException(
                    $"Trade operation '{operationId}' cannot need attention without an attention item.");
        }
        else if (expectedOperationState is TradeOperationState.NeedsAttention &&
                 nextOperationState is TradeOperationState.Running)
        {
            var attentionItems = Convert.ToInt32(ExecuteScalar(
                connection,
                transaction,
                """
                SELECT COUNT(*)
                FROM trade_plan_items
                WHERE plan_id = $plan_id AND state = 'needs_attention';
                """,
                ("$plan_id", planId)));
            if (attentionItems != 0)
                throw new TradeStoreConflictException(
                    $"Trade operation '{operationId}' cannot resume while an item still needs attention.");
        }
        else if (nextOperationState is TradeOperationState.Completed)
        {
            var unfinishedItems = Convert.ToInt32(ExecuteScalar(
                connection,
                transaction,
                """
                SELECT COUNT(*)
                FROM trade_plan_items
                WHERE plan_id = $plan_id AND state NOT IN ('completed', 'skipped');
                """,
                ("$plan_id", planId)));
            if (unfinishedItems != 0)
                throw new TradeStoreConflictException(
                    $"Trade operation '{operationId}' cannot complete while {unfinishedItems} item(s) are unfinished.");
        }

        var timestamp = occurredAt.ToUnixTimeMilliseconds();
        var operationChanged = ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE trade_operations
            SET state = $next_state, updated_at_ms = $updated, version = version + 1
            WHERE operation_id = $operation_id AND state = $expected_state;
            """,
            ("$next_state", ToStoreValue(nextOperationState)),
            ("$updated", timestamp),
            ("$operation_id", operationId),
            ("$expected_state", ToStoreValue(expectedOperationState)));

        if (operationChanged != 1)
            throw new TradeStoreConcurrencyException(
                $"Trade operation '{operationId}' is no longer in state '{expectedOperationState}'.");

        var planChanged = ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE trade_plans
            SET state = $next_state, updated_at_ms = $updated, version = version + 1
            WHERE plan_id = $plan_id AND state = $expected_state;
            """,
            ("$next_state", ToStoreValue(nextPlanState)),
            ("$updated", timestamp),
            ("$plan_id", planId),
            ("$expected_state", ToStoreValue(expectedPlanState)));

        if (planChanged != 1)
            throw new TradeStoreConcurrencyException(
                $"Trade plan '{planId}' is no longer in state '{expectedPlanState}'.");

        AppendEvent(
            connection,
            transaction,
            planId,
            operationId,
            null,
            eventType,
            detailsJson,
            occurredAt);

        var updated = ReadOperation(connection, transaction, operationId)
            ?? throw new TradeStoreNotFoundException(
                $"Trade operation '{operationId}' was not found after transition.");
        transaction.Commit();
        return updated;
    }

    public TradePlanItemSnapshot TransitionItem(
        string operationId,
        TradeOperationState expectedOperationState,
        string itemId,
        TradePlanItemState expectedState,
        TradePlanItemState nextState,
        string eventType,
        string detailsJson,
        DateTimeOffset occurredAt,
        string? lastErrorJson = null,
        string? settlementEvidenceJson = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        ValidateTransition(expectedState, nextState);
        ValidateEvent(eventType, detailsJson);
        if (lastErrorJson is not null)
            ValidateJson(lastErrorJson, nameof(lastErrorJson));
        if (settlementEvidenceJson is not null)
            ValidateJson(settlementEvidenceJson, nameof(settlementEvidenceJson));

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var operation = ReadOperation(connection, transaction, operationId);
        if (operation is null)
            throw new TradeStoreNotFoundException($"Trade operation '{operationId}' was not found.");
        if (operation.State != expectedOperationState)
            throw new TradeStoreConcurrencyException(
                $"Trade operation '{operationId}' is no longer in state '{expectedOperationState}'.");
        var planId = operation.PlanId;
        var storedPlanState = ParsePlanState(Convert.ToString(ExecuteScalar(
            connection,
            transaction,
            "SELECT state FROM trade_plans WHERE plan_id = $plan_id;",
            ("$plan_id", planId))) ?? string.Empty);
        var expectedPlanState = ExpectedPlanState(expectedOperationState);
        if (storedPlanState != expectedPlanState)
            throw new TradeStoreConcurrencyException(
                $"Trade plan '{planId}' is no longer in state '{expectedPlanState}'.");

        var timestamp = occurredAt.ToUnixTimeMilliseconds();
        var changed = ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE trade_plan_items
            SET state = $next_state,
                last_error_json = CASE
                    WHEN $last_error_json IS NULL THEN last_error_json
                    ELSE $last_error_json
                END,
                settlement_evidence_json = CASE
                    WHEN $settlement_evidence_json IS NULL THEN settlement_evidence_json
                    ELSE $settlement_evidence_json
                END,
                updated_at_ms = $updated,
                version = version + 1
            WHERE item_id = $item_id AND plan_id = $plan_id AND state = $expected_state;
            """,
            ("$next_state", ToStoreValue(nextState)),
            ("$last_error_json", lastErrorJson),
            ("$settlement_evidence_json", settlementEvidenceJson),
            ("$updated", timestamp),
            ("$item_id", itemId),
            ("$plan_id", planId),
            ("$expected_state", ToStoreValue(expectedState)));

        if (changed != 1)
            throw new TradeStoreConcurrencyException(
                $"Trade item '{itemId}' is missing or is no longer in state '{expectedState}'.");

        if (nextState is TradePlanItemState.NeedsAttention)
        {
            var operationChanged = ExecuteNonQuery(
                connection,
                transaction,
                """
                UPDATE trade_operations
                SET state = 'needs_attention',
                    current_item_id = $item_id,
                    updated_at_ms = $updated,
                    version = version + 1
                WHERE operation_id = $operation_id AND state = $expected_operation_state;
                """,
                ("$item_id", itemId),
                ("$updated", timestamp),
                ("$operation_id", operationId),
                ("$expected_operation_state", ToStoreValue(expectedOperationState)));
            if (operationChanged != 1)
                throw new TradeStoreConcurrencyException(
                    $"Trade operation '{operationId}' changed while escalating attention.");

            var planChanged = ExecuteNonQuery(
                connection,
                transaction,
                """
                UPDATE trade_plans
                SET state = 'needs_attention', updated_at_ms = $updated, version = version + 1
                WHERE plan_id = $plan_id AND state = 'running';
                """,
                ("$updated", timestamp),
                ("$plan_id", planId));
            if (planChanged != 1)
                throw new TradeStoreConcurrencyException(
                    $"Trade plan '{planId}' changed while escalating attention.");
        }
        else
        {
            ExecuteNonQuery(
                connection,
                transaction,
                """
                UPDATE trade_operations
                SET current_item_id = $item_id, updated_at_ms = $updated, version = version + 1
                WHERE operation_id = $operation_id AND state = $expected_operation_state;
                UPDATE trade_plans
                SET updated_at_ms = $updated, version = version + 1
                WHERE plan_id = $plan_id;
                """,
                ("$item_id", itemId),
                ("$updated", timestamp),
                ("$operation_id", operationId),
                ("$expected_operation_state", ToStoreValue(expectedOperationState)),
                ("$plan_id", planId));
        }

        AppendEvent(
            connection,
            transaction,
            planId,
            operationId,
            itemId,
            eventType,
            detailsJson,
            occurredAt);

        var updated = ReadItem(connection, transaction, itemId)
            ?? throw new TradeStoreNotFoundException(
                $"Trade item '{itemId}' was not found after transition.");
        transaction.Commit();
        return updated;
    }

    public TradeAttemptSnapshot StartAttempt(
        string attemptId,
        string operationId,
        string itemId,
        int attemptNumber,
        DateTimeOffset startedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        if (attemptNumber <= 0)
            throw new ArgumentOutOfRangeException(nameof(attemptNumber));

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var operation = ReadOperation(connection, transaction, operationId)
            ?? throw new TradeStoreNotFoundException($"Trade operation '{operationId}' was not found.");
        if (operation.State is not TradeOperationState.Running)
            throw new TradeStoreConcurrencyException(
                $"Trade operation '{operationId}' cannot start an attempt while '{operation.State}'.");
        var planId = operation.PlanId;
        var storedPlanState = ParsePlanState(Convert.ToString(ExecuteScalar(
            connection,
            transaction,
            "SELECT state FROM trade_plans WHERE plan_id = $plan_id;",
            ("$plan_id", planId))) ?? string.Empty);
        if (storedPlanState is not TradePlanState.Running)
            throw new TradeStoreConcurrencyException(
                $"Trade plan '{planId}' cannot start an attempt while '{storedPlanState}'.");
        var timestamp = startedAt.ToUnixTimeMilliseconds();

        var itemChanged = ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE trade_plan_items
            SET attempt_count = $attempt_number,
                updated_at_ms = $updated,
                version = version + 1
            WHERE item_id = $item_id
              AND plan_id = $plan_id
              AND state = 'searching'
              AND attempt_count = $attempt_number - 1;
            """,
            ("$attempt_number", attemptNumber),
            ("$updated", timestamp),
            ("$item_id", itemId),
            ("$plan_id", planId));

        if (itemChanged != 1)
            throw new TradeStoreConcurrencyException(
                $"Trade item '{itemId}' cannot start attempt {attemptNumber}.");

        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO trade_attempts (
                attempt_id, operation_id, item_id, attempt_number,
                started_at_ms, ended_at_ms, failure_code,
                irreversible_boundary_crossed
            )
            VALUES (
                $attempt_id, $operation_id, $item_id, $attempt_number,
                $started, NULL, NULL, 0
            );
            """,
            ("$attempt_id", attemptId),
            ("$operation_id", operationId),
            ("$item_id", itemId),
            ("$attempt_number", attemptNumber),
            ("$started", timestamp));

        AppendEvent(
            connection,
            transaction,
            planId,
            operationId,
            itemId,
            "attempt_started",
            JsonSerializer.Serialize(new { attempt_id = attemptId, attempt_number = attemptNumber }),
            startedAt);

        var created = ReadAttempt(connection, transaction, attemptId)
            ?? throw new TradeStoreNotFoundException($"Trade attempt '{attemptId}' was not found.");
        transaction.Commit();
        return created;
    }

    public TradeAttemptSnapshot FinishAttempt(
        string attemptId,
        DateTimeOffset endedAt,
        string? failureCode,
        bool irreversibleBoundaryCrossed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attemptId);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var current = ReadAttempt(connection, transaction, attemptId)
            ?? throw new TradeStoreNotFoundException($"Trade attempt '{attemptId}' was not found.");

        if (current.EndedAt is not null)
            throw new TradeStoreConcurrencyException($"Trade attempt '{attemptId}' has already ended.");

        var changed = ExecuteNonQuery(
            connection,
            transaction,
            """
            UPDATE trade_attempts
            SET ended_at_ms = $ended,
                failure_code = $failure_code,
                irreversible_boundary_crossed = $irreversible
            WHERE attempt_id = $attempt_id AND ended_at_ms IS NULL;
            """,
            ("$ended", endedAt.ToUnixTimeMilliseconds()),
            ("$failure_code", failureCode),
            ("$irreversible", irreversibleBoundaryCrossed ? 1 : 0),
            ("$attempt_id", attemptId));

        if (changed != 1)
            throw new TradeStoreConcurrencyException($"Trade attempt '{attemptId}' was concurrently ended.");

        var planId = ReadOperationPlanId(connection, transaction, current.OperationId);
        AppendEvent(
            connection,
            transaction,
            planId,
            current.OperationId,
            current.ItemId,
            "attempt_finished",
            JsonSerializer.Serialize(new
            {
                attempt_id = attemptId,
                failure_code = failureCode,
                irreversible_boundary_crossed = irreversibleBoundaryCrossed,
            }),
            endedAt);

        var finished = ReadAttempt(connection, transaction, attemptId)
            ?? throw new TradeStoreNotFoundException($"Trade attempt '{attemptId}' was not found.");
        transaction.Commit();
        return finished;
    }

    public IReadOnlyList<TradeAttemptSnapshot> GetAttempts(string itemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        using var connection = OpenConnection();
        using var command = CreateCommand(
            connection,
            null,
            """
            SELECT attempt_id, operation_id, item_id, attempt_number,
                   started_at_ms, ended_at_ms, failure_code,
                   irreversible_boundary_crossed
            FROM trade_attempts
            WHERE item_id = $item_id
            ORDER BY attempt_number;
            """,
            ("$item_id", itemId));
        using var reader = command.ExecuteReader();
        var attempts = new List<TradeAttemptSnapshot>();
        while (reader.Read())
            attempts.Add(ReadAttempt(reader));
        return attempts;
    }

    public IReadOnlyList<TradeEventSnapshot> ListPlanEvents(
        string planId,
        long afterSequence = 0,
        int limit = 200)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(planId);
        ValidatePage(afterSequence, limit);
        using var connection = OpenConnection();
        return ReadEvents(
            connection,
            "plan_id = $id",
            planId,
            afterSequence,
            limit);
    }

    public IReadOnlyList<TradeEventSnapshot> ListEvents(
        string operationId,
        long afterSequence = 0,
        int limit = 200)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ValidatePage(afterSequence, limit);
        using var connection = OpenConnection();
        return ReadEvents(
            connection,
            "operation_id = $id",
            operationId,
            afterSequence,
            limit);
    }

    public TradeLeaseAcquireResult TryAcquireLease(
        string botInstanceId,
        string operationId,
        string ownerTokenHash,
        DateTimeOffset acquiredAt,
        DateTimeOffset expiresAt)
    {
        ValidateLease(botInstanceId, operationId, ownerTokenHash, acquiredAt, expiresAt);

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        var operation = ReadOperation(connection, transaction, operationId)
            ?? throw new TradeStoreNotFoundException($"Trade operation '{operationId}' was not found.");
        if (IsTerminal(operation.State))
            throw new TradeStoreConflictException(
                $"A lease cannot be acquired for terminal operation '{operationId}'.");
        var changed = ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO trade_leases (
                bot_instance_id, operation_id, owner_token_hash,
                acquired_at_ms, expires_at_ms, revision
            )
            VALUES (
                $bot_id, $operation_id, $owner_hash,
                $acquired, $expires, 1
            )
            ON CONFLICT(bot_instance_id) DO UPDATE SET
                operation_id = excluded.operation_id,
                owner_token_hash = excluded.owner_token_hash,
                acquired_at_ms = excluded.acquired_at_ms,
                expires_at_ms = excluded.expires_at_ms,
                revision = trade_leases.revision + 1
            WHERE trade_leases.expires_at_ms <= $acquired
               OR (
                    trade_leases.operation_id = $operation_id
                    AND trade_leases.owner_token_hash = $owner_hash
               );
            """,
            ("$bot_id", botInstanceId),
            ("$operation_id", operationId),
            ("$owner_hash", ownerTokenHash),
            ("$acquired", acquiredAt.ToUnixTimeMilliseconds()),
            ("$expires", expiresAt.ToUnixTimeMilliseconds()));

        var current = ReadLease(connection, transaction, botInstanceId)
            ?? throw new TradeStoreNotFoundException(
                $"Lease row for bot '{botInstanceId}' was not found after acquisition.");
        var acquired = changed == 1 &&
                       current.OperationId == operationId &&
                       current.OwnerTokenHash == ownerTokenHash;
        transaction.Commit();
        return new(acquired, current);
    }

    public bool RenewLease(
        string botInstanceId,
        string operationId,
        string ownerTokenHash,
        DateTimeOffset now,
        DateTimeOffset newExpiresAt)
    {
        ValidateLease(botInstanceId, operationId, ownerTokenHash, now, newExpiresAt);
        using var connection = OpenConnection();
        var changed = ExecuteNonQuery(
            connection,
            null,
            """
            UPDATE trade_leases
            SET expires_at_ms = $new_expires, revision = revision + 1
            WHERE bot_instance_id = $bot_id
              AND operation_id = $operation_id
              AND owner_token_hash = $owner_hash
              AND expires_at_ms > $now
              AND EXISTS (
                  SELECT 1
                  FROM trade_operations
                  WHERE operation_id = $operation_id
                    AND state NOT IN ('completed', 'failed', 'cancelled')
              );
            """,
            ("$new_expires", newExpiresAt.ToUnixTimeMilliseconds()),
            ("$bot_id", botInstanceId),
            ("$operation_id", operationId),
            ("$owner_hash", ownerTokenHash),
            ("$now", now.ToUnixTimeMilliseconds()));
        return changed == 1;
    }

    public bool ReleaseLease(
        string botInstanceId,
        string operationId,
        string ownerTokenHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(botInstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerTokenHash);
        using var connection = OpenConnection();
        return ExecuteNonQuery(
            connection,
            null,
            """
            DELETE FROM trade_leases
            WHERE bot_instance_id = $bot_id
              AND operation_id = $operation_id
              AND owner_token_hash = $owner_hash;
            """,
            ("$bot_id", botInstanceId),
            ("$operation_id", operationId),
            ("$owner_hash", ownerTokenHash)) == 1;
    }

    public TradeLeaseSnapshot? GetLease(string botInstanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(botInstanceId);
        using var connection = OpenConnection();
        return ReadLease(connection, null, botInstanceId);
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        ExecuteNonQuery(
            connection,
            null,
            """
            PRAGMA busy_timeout = 5000;
            PRAGMA synchronous = FULL;
            """);
        return connection;
    }

    private static TradePlanSnapshot? ReadPlan(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string planId)
    {
        using var command = CreateCommand(
            connection,
            transaction,
            """
            SELECT plan_id, owner_id, game_mode, state, access_json,
                   evolution_policy, partner_disconnect_max_attempts,
                   transport_reconnect_delays_json, retry_exhausted_policy,
                   uncertain_settlement_policy, validation_runtime_generation,
                   created_at_ms, updated_at_ms, version
            FROM trade_plans
            WHERE plan_id = $plan_id;
            """,
            ("$plan_id", planId));

        string storedPlanId;
        string ownerId;
        ProgramMode gameMode;
        TradePlanState state;
        string accessJson;
        TradePlanPolicies policies;
        string? runtimeGeneration;
        DateTimeOffset createdAt;
        DateTimeOffset updatedAt;
        long version;

        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read())
                return null;

            storedPlanId = reader.GetString(0);
            ownerId = reader.GetString(1);
            gameMode = ParseProgramMode(reader.GetString(2));
            state = ParsePlanState(reader.GetString(3));
            accessJson = reader.GetString(4);
            policies = new TradePlanPolicies
            {
                Evolution = ParseEvolutionPolicy(reader.GetString(5)),
                PartnerDisconnectMaxAttempts = reader.GetInt32(6),
                TransportReconnectDelaysMs =
                    JsonSerializer.Deserialize<int[]>(reader.GetString(7)) ?? [],
                OnRetryExhausted = ParseRetryExhaustedPolicy(reader.GetString(8)),
                OnUncertainSettlement =
                    ParseUncertainSettlementPolicy(reader.GetString(9)),
            };
            runtimeGeneration = reader.IsDBNull(10) ? null : reader.GetString(10);
            createdAt = FromUnixMilliseconds(reader.GetInt64(11));
            updatedAt = FromUnixMilliseconds(reader.GetInt64(12));
            version = reader.GetInt64(13);
        }

        using var itemCommand = CreateCommand(
            connection,
            transaction,
            """
            SELECT item_id, plan_id, client_item_id, position, showdown_set,
                   state, prepared_hash, attempt_count, last_error_json,
                   settlement_evidence_json, created_at_ms, updated_at_ms, version
            FROM trade_plan_items
            WHERE plan_id = $plan_id
            ORDER BY position;
            """,
            ("$plan_id", planId));
        using var itemReader = itemCommand.ExecuteReader();
        var items = new List<TradePlanItemSnapshot>();
        while (itemReader.Read())
            items.Add(ReadItem(itemReader));

        return new(
            storedPlanId,
            ownerId,
            gameMode,
            state,
            accessJson,
            policies,
            runtimeGeneration,
            createdAt,
            updatedAt,
            version,
            items);
    }

    private static TradePlanItemSnapshot? ReadItem(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string itemId)
    {
        using var command = CreateCommand(
            connection,
            transaction,
            """
            SELECT item_id, plan_id, client_item_id, position, showdown_set,
                   state, prepared_hash, attempt_count, last_error_json,
                   settlement_evidence_json, created_at_ms, updated_at_ms, version
            FROM trade_plan_items
            WHERE item_id = $item_id;
            """,
            ("$item_id", itemId));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadItem(reader) : null;
    }

    private static TradePlanItemSnapshot ReadItem(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            reader.GetString(4),
            ParseItemState(reader.GetString(5)),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetInt32(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            FromUnixMilliseconds(reader.GetInt64(10)),
            FromUnixMilliseconds(reader.GetInt64(11)),
            reader.GetInt64(12));

    private static TradeOperationSnapshot? ReadOperation(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string operationId)
    {
        using var command = CreateCommand(
            connection,
            transaction,
            """
            SELECT operation_id, plan_id, state, current_item_id,
                   created_at_ms, updated_at_ms, version
            FROM trade_operations
            WHERE operation_id = $operation_id;
            """,
            ("$operation_id", operationId));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadOperation(reader) : null;
    }

    private static TradeOperationSnapshot ReadOperation(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            ParseOperationState(reader.GetString(2)),
            reader.IsDBNull(3) ? null : reader.GetString(3),
            FromUnixMilliseconds(reader.GetInt64(4)),
            FromUnixMilliseconds(reader.GetInt64(5)),
            reader.GetInt64(6));

    private static TradeAttemptSnapshot? ReadAttempt(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string attemptId)
    {
        using var command = CreateCommand(
            connection,
            transaction,
            """
            SELECT attempt_id, operation_id, item_id, attempt_number,
                   started_at_ms, ended_at_ms, failure_code,
                   irreversible_boundary_crossed
            FROM trade_attempts
            WHERE attempt_id = $attempt_id;
            """,
            ("$attempt_id", attemptId));
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadAttempt(reader) : null;
    }

    private static TradeAttemptSnapshot ReadAttempt(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            FromUnixMilliseconds(reader.GetInt64(4)),
            reader.IsDBNull(5) ? null : FromUnixMilliseconds(reader.GetInt64(5)),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetInt32(7) == 1);

    private static TradeLeaseSnapshot? ReadLease(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string botInstanceId)
    {
        using var command = CreateCommand(
            connection,
            transaction,
            """
            SELECT bot_instance_id, operation_id, owner_token_hash,
                   acquired_at_ms, expires_at_ms, revision
            FROM trade_leases
            WHERE bot_instance_id = $bot_id;
            """,
            ("$bot_id", botInstanceId));
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                FromUnixMilliseconds(reader.GetInt64(3)),
                FromUnixMilliseconds(reader.GetInt64(4)),
                reader.GetInt64(5))
            : null;
    }

    private static IReadOnlyList<TradeEventSnapshot> ReadEvents(
        SqliteConnection connection,
        string predicate,
        string id,
        long afterSequence,
        int limit)
    {
        using var command = CreateCommand(
            connection,
            null,
            $"""
            SELECT event_id, sequence, operation_id, plan_id, item_id,
                   event_type, details_json, occurred_at_ms
            FROM trade_events
            WHERE {predicate} AND sequence > $after_sequence
            ORDER BY sequence
            LIMIT $limit;
            """,
            ("$id", id),
            ("$after_sequence", afterSequence),
            ("$limit", limit));
        using var reader = command.ExecuteReader();
        var events = new List<TradeEventSnapshot>();
        while (reader.Read())
        {
            events.Add(new(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                FromUnixMilliseconds(reader.GetInt64(7))));
        }

        return events;
    }

    private static void AppendEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string planId,
        string? operationId,
        string? itemId,
        string eventType,
        string detailsJson,
        DateTimeOffset occurredAt)
    {
        ValidateEvent(eventType, detailsJson);
        var sequence = Convert.ToInt64(ExecuteScalar(
            connection,
            transaction,
            """
            SELECT COALESCE(MAX(sequence), 0) + 1
            FROM trade_events
            WHERE plan_id = $plan_id;
            """,
            ("$plan_id", planId)));

        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO trade_events (
                sequence, operation_id, plan_id, item_id,
                event_type, details_json, occurred_at_ms
            )
            VALUES (
                $sequence, $operation_id, $plan_id, $item_id,
                $event_type, $details_json, $occurred_at
            );
            """,
            ("$sequence", sequence),
            ("$operation_id", operationId),
            ("$plan_id", planId),
            ("$item_id", itemId),
            ("$event_type", eventType),
            ("$details_json", detailsJson),
            ("$occurred_at", occurredAt.ToUnixTimeMilliseconds()));
    }

    private static string ReadOperationPlanId(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string operationId)
    {
        var planId = Convert.ToString(ExecuteScalar(
            connection,
            transaction,
            "SELECT plan_id FROM trade_operations WHERE operation_id = $operation_id;",
            ("$operation_id", operationId)));
        return !string.IsNullOrWhiteSpace(planId)
            ? planId
            : throw new TradeStoreNotFoundException($"Trade operation '{operationId}' was not found.");
    }

    private static IdempotencyRecord? FindIdempotency(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string scope,
        string idempotencyKey)
    {
        using var command = CreateCommand(
            connection,
            transaction,
            """
            SELECT request_hash, resource_type, resource_id
            FROM trade_idempotency
            WHERE scope = $scope AND idempotency_key = $key;
            """,
            ("$scope", scope),
            ("$key", idempotencyKey));
        using var reader = command.ExecuteReader();
        return reader.Read()
            ? new(reader.GetString(0), reader.GetString(1), reader.GetString(2))
            : null;
    }

    private static void InsertIdempotency(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string scope,
        string idempotencyKey,
        string requestHash,
        string resourceType,
        string resourceId,
        DateTimeOffset createdAt) =>
        ExecuteNonQuery(
            connection,
            transaction,
            """
            INSERT INTO trade_idempotency (
                scope, idempotency_key, request_hash,
                resource_type, resource_id, created_at_ms
            )
            VALUES (
                $scope, $key, $request_hash,
                $resource_type, $resource_id, $created
            );
            """,
            ("$scope", scope),
            ("$key", idempotencyKey),
            ("$request_hash", requestHash),
            ("$resource_type", resourceType),
            ("$resource_id", resourceId),
            ("$created", createdAt.ToUnixTimeMilliseconds()));

    private static void EnsureIdempotencyMatch(
        IdempotencyRecord existing,
        string requestHash,
        string resourceType)
    {
        if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal) ||
            !string.Equals(existing.ResourceType, resourceType, StringComparison.Ordinal))
        {
            throw new TradeStoreConflictException(
                "The idempotency key is already associated with a different request.");
        }
    }

    private static SqliteCommand CreateCommand(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private static int ExecuteNonQuery(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using var command = CreateCommand(connection, transaction, sql, parameters);
        return command.ExecuteNonQuery();
    }

    private static object? ExecuteScalar(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        using var command = CreateCommand(connection, transaction, sql, parameters);
        return command.ExecuteScalar();
    }

    private static void ValidateDraft(TradePlanDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.PlanId);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.OwnerId);
        if (draft.OwnerId.Length > 128)
            throw new ArgumentOutOfRangeException(nameof(draft), "Owner IDs cannot exceed 128 characters.");
        if (draft.GameMode is ProgramMode.None)
            throw new ArgumentException("A supported game mode is required.", nameof(draft));
        ValidateJson(draft.AccessJson, nameof(draft.AccessJson));
        if (draft.Items.Count is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(draft), "A plan must contain 1 to 100 items.");
        if (draft.Policies.Validate().Count != 0)
            throw new ArgumentException("Trade plan policies are invalid.", nameof(draft));

        var ordered = draft.Items.OrderBy(item => item.Position).ToArray();
        for (int i = 0; i < ordered.Length; i++)
        {
            var item = ordered[i];
            ArgumentException.ThrowIfNullOrWhiteSpace(item.ItemId);
            ArgumentException.ThrowIfNullOrWhiteSpace(item.ClientItemId);
            ArgumentException.ThrowIfNullOrWhiteSpace(item.ShowdownSet);
            if (item.ClientItemId.Length > 80)
                throw new ArgumentOutOfRangeException(nameof(draft), "Client item IDs cannot exceed 80 characters.");
            if (item.ShowdownSet.Length > 8192)
                throw new ArgumentOutOfRangeException(nameof(draft), "Showdown sets cannot exceed 8192 characters.");
            if (item.Position != i)
                throw new ArgumentException(
                    "Trade plan item positions must be contiguous and zero-based.",
                    nameof(draft));
        }

        if (draft.Items.Select(item => item.ItemId).Distinct(StringComparer.Ordinal).Count() != draft.Items.Count)
            throw new ArgumentException("Trade plan item IDs must be unique.", nameof(draft));
        if (draft.Items.Select(item => item.ClientItemId).Distinct(StringComparer.Ordinal).Count() != draft.Items.Count)
            throw new ArgumentException("Trade plan client item IDs must be unique.", nameof(draft));
    }

    private static void ValidateIdempotency(string scope, string key, string requestHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        if (key.Length is < 8 or > 128)
            throw new ArgumentOutOfRangeException(nameof(key), "Idempotency keys must be 8 to 128 characters.");
    }

    private static void ValidateEvent(string eventType, string detailsJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        if (eventType.Length is < 3 or > 80)
            throw new ArgumentOutOfRangeException(nameof(eventType));
        ValidateJson(detailsJson, nameof(detailsJson));
    }

    private static void ValidateJson(string json, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json, parameterName);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind is not JsonValueKind.Object)
                throw new ArgumentException("Value must be a JSON object.", parameterName);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Value must be valid JSON.", parameterName, ex);
        }
    }

    private static void ValidateTransition(TradePlanState current, TradePlanState next)
    {
        if (!current.CanTransitionTo(next))
            throw new InvalidOperationException(
                $"Trade plan state cannot transition from '{current}' to '{next}'.");
    }

    private static void ValidateTransition(TradePlanItemState current, TradePlanItemState next)
    {
        if (!current.CanTransitionTo(next))
            throw new InvalidOperationException(
                $"Trade item state cannot transition from '{current}' to '{next}'.");
    }

    private static void ValidateTransition(TradeOperationState current, TradeOperationState next)
    {
        if (!current.CanTransitionTo(next))
            throw new InvalidOperationException(
                $"Trade operation state cannot transition from '{current}' to '{next}'.");
    }

    private static void ValidatePage(long afterSequence, int limit)
    {
        if (afterSequence < 0)
            throw new ArgumentOutOfRangeException(nameof(afterSequence));
        if (limit is < 1 or > 200)
            throw new ArgumentOutOfRangeException(nameof(limit));
    }

    private static void ValidateLease(
        string botInstanceId,
        string operationId,
        string ownerTokenHash,
        DateTimeOffset acquiredAt,
        DateTimeOffset expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(botInstanceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerTokenHash);
        if (expiresAt <= acquiredAt)
            throw new ArgumentOutOfRangeException(nameof(expiresAt));
    }

    private static DateTimeOffset FromUnixMilliseconds(long value) =>
        DateTimeOffset.FromUnixTimeMilliseconds(value);

    private static TradePlanState ExpectedPlanState(TradeOperationState operationState) =>
        operationState switch
        {
            TradeOperationState.Queued => TradePlanState.Queued,
            TradeOperationState.Running => TradePlanState.Running,
            TradeOperationState.Paused => TradePlanState.Paused,
            TradeOperationState.NeedsAttention => TradePlanState.NeedsAttention,
            TradeOperationState.Completed => TradePlanState.Completed,
            TradeOperationState.Failed => TradePlanState.Failed,
            TradeOperationState.Cancelled => TradePlanState.Cancelled,
            _ => throw new ArgumentOutOfRangeException(nameof(operationState)),
        };

    private static bool IsTerminal(TradeOperationState state) =>
        state is TradeOperationState.Completed or
            TradeOperationState.Failed or
            TradeOperationState.Cancelled;

    private static string ToStoreValue(ProgramMode value) => value switch
    {
        ProgramMode.SWSH => "swsh",
        ProgramMode.BDSP => "bdsp",
        ProgramMode.LA => "la",
        ProgramMode.SV => "sv",
        ProgramMode.LGPE => "lgpe",
        ProgramMode.LZA => "lza",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static ProgramMode ParseProgramMode(string value) => value switch
    {
        "swsh" => ProgramMode.SWSH,
        "bdsp" => ProgramMode.BDSP,
        "la" => ProgramMode.LA,
        "sv" => ProgramMode.SV,
        "lgpe" => ProgramMode.LGPE,
        "lza" => ProgramMode.LZA,
        _ => throw new InvalidDataException($"Unknown stored game mode '{value}'."),
    };

    private static string ToStoreValue(TradePlanState value) => value switch
    {
        TradePlanState.Draft => "draft",
        TradePlanState.Validated => "validated",
        TradePlanState.Queued => "queued",
        TradePlanState.Running => "running",
        TradePlanState.Paused => "paused",
        TradePlanState.NeedsAttention => "needs_attention",
        TradePlanState.Completed => "completed",
        TradePlanState.Failed => "failed",
        TradePlanState.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static TradePlanState ParsePlanState(string value) => value switch
    {
        "draft" => TradePlanState.Draft,
        "validated" => TradePlanState.Validated,
        "queued" => TradePlanState.Queued,
        "running" => TradePlanState.Running,
        "paused" => TradePlanState.Paused,
        "needs_attention" => TradePlanState.NeedsAttention,
        "completed" => TradePlanState.Completed,
        "failed" => TradePlanState.Failed,
        "cancelled" => TradePlanState.Cancelled,
        _ => throw new InvalidDataException($"Unknown stored trade plan state '{value}'."),
    };

    private static string ToStoreValue(TradePlanItemState value) => value switch
    {
        TradePlanItemState.Pending => "pending",
        TradePlanItemState.Prepared => "prepared",
        TradePlanItemState.Searching => "searching",
        TradePlanItemState.PartnerFound => "partner_found",
        TradePlanItemState.Offered => "offered",
        TradePlanItemState.Confirming => "confirming",
        TradePlanItemState.Settling => "settling",
        TradePlanItemState.NeedsAttention => "needs_attention",
        TradePlanItemState.Completed => "completed",
        TradePlanItemState.Skipped => "skipped",
        TradePlanItemState.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static TradePlanItemState ParseItemState(string value) => value switch
    {
        "pending" => TradePlanItemState.Pending,
        "prepared" => TradePlanItemState.Prepared,
        "searching" => TradePlanItemState.Searching,
        "partner_found" => TradePlanItemState.PartnerFound,
        "offered" => TradePlanItemState.Offered,
        "confirming" => TradePlanItemState.Confirming,
        "settling" => TradePlanItemState.Settling,
        "needs_attention" => TradePlanItemState.NeedsAttention,
        "completed" => TradePlanItemState.Completed,
        "skipped" => TradePlanItemState.Skipped,
        "failed" => TradePlanItemState.Failed,
        _ => throw new InvalidDataException($"Unknown stored trade item state '{value}'."),
    };

    private static string ToStoreValue(TradeOperationState value) => value switch
    {
        TradeOperationState.Queued => "queued",
        TradeOperationState.Running => "running",
        TradeOperationState.Paused => "paused",
        TradeOperationState.NeedsAttention => "needs_attention",
        TradeOperationState.Completed => "completed",
        TradeOperationState.Failed => "failed",
        TradeOperationState.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static TradeOperationState ParseOperationState(string value) => value switch
    {
        "queued" => TradeOperationState.Queued,
        "running" => TradeOperationState.Running,
        "paused" => TradeOperationState.Paused,
        "needs_attention" => TradeOperationState.NeedsAttention,
        "completed" => TradeOperationState.Completed,
        "failed" => TradeOperationState.Failed,
        "cancelled" => TradeOperationState.Cancelled,
        _ => throw new InvalidDataException($"Unknown stored trade operation state '{value}'."),
    };

    private static string ToStoreValue(TradeEvolutionPolicy value) => value switch
    {
        TradeEvolutionPolicy.Block => "block",
        TradeEvolutionPolicy.AllowManual => "allow_manual",
        TradeEvolutionPolicy.AllowAndHandle => "allow_and_handle",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static TradeEvolutionPolicy ParseEvolutionPolicy(string value) => value switch
    {
        "block" => TradeEvolutionPolicy.Block,
        "allow_manual" => TradeEvolutionPolicy.AllowManual,
        "allow_and_handle" => TradeEvolutionPolicy.AllowAndHandle,
        _ => throw new InvalidDataException($"Unknown stored evolution policy '{value}'."),
    };

    private static string ToStoreValue(TradeRetryExhaustedPolicy value) => value switch
    {
        TradeRetryExhaustedPolicy.Pause => "pause",
        TradeRetryExhaustedPolicy.SkipItem => "skip_item",
        TradeRetryExhaustedPolicy.CancelPlan => "cancel_plan",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static TradeRetryExhaustedPolicy ParseRetryExhaustedPolicy(string value) => value switch
    {
        "pause" => TradeRetryExhaustedPolicy.Pause,
        "skip_item" => TradeRetryExhaustedPolicy.SkipItem,
        "cancel_plan" => TradeRetryExhaustedPolicy.CancelPlan,
        _ => throw new InvalidDataException($"Unknown stored retry policy '{value}'."),
    };

    private static string ToStoreValue(TradeUncertainSettlementPolicy value) => value switch
    {
        TradeUncertainSettlementPolicy.NeedsAttention => "needs_attention",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static TradeUncertainSettlementPolicy ParseUncertainSettlementPolicy(string value) =>
        value switch
        {
            "needs_attention" => TradeUncertainSettlementPolicy.NeedsAttention,
            _ => throw new InvalidDataException(
                $"Unknown stored uncertain settlement policy '{value}'."),
        };

    private sealed record IdempotencyRecord(
        string RequestHash,
        string ResourceType,
        string ResourceId);
}
