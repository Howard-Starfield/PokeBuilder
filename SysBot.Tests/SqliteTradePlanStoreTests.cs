using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using SysBot.Pokemon;
using Xunit;

namespace SysBot.Tests;

public class SqliteTradePlanStoreTests
{
    private static readonly DateTimeOffset Baseline =
        DateTimeOffset.FromUnixTimeMilliseconds(1_785_283_200_000);

    [Fact]
    public void Initialize_IsIdempotentAndEnablesWal()
    {
        using var database = new TemporaryTradeDatabase();
        var store = database.CreateStore();

        store.Initialize();
        store.Initialize();

        store.GetSchemaVersion().Should().Be(1);

        using var connection = new SqliteConnection($"Data Source={database.DatabasePath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";
        Convert.ToString(command.ExecuteScalar()).Should().Be("wal");
    }

    [Fact]
    public void CreatePlan_RoundTripsAcrossRestartAndReplaysIdempotently()
    {
        using var database = new TemporaryTradeDatabase();
        var firstStore = database.CreateStore();
        firstStore.Initialize();
        var draft = CreateDraft("alpha");

        var created = firstStore.CreatePlan(
            draft,
            "owner:local-test",
            "create-alpha-0001",
            "sha256:create-alpha");

        created.Outcome.Should().Be(TradeStoreIdempotencyOutcome.Created);
        created.Resource.State.Should().Be(TradePlanState.Draft);
        created.Resource.Items.Should().HaveCount(2);

        var restartedStore = database.CreateStore();
        restartedStore.Initialize();
        var restored = restartedStore.GetPlan(draft.PlanId);

        restored.Should().NotBeNull();
        restored!.GameMode.Should().Be(ProgramMode.LZA);
        restored.Policies.Evolution.Should().Be(TradeEvolutionPolicy.Block);
        restored.Policies.TransportReconnectDelaysMs
            .Should().Equal(0, 250, 1_000, 5_000, 30_000);
        restored.Items.Select(item => item.Position).Should().Equal(0, 1);

        var replayed = restartedStore.CreatePlan(
            draft,
            "owner:local-test",
            "create-alpha-0001",
            "sha256:create-alpha");
        replayed.Outcome.Should().Be(TradeStoreIdempotencyOutcome.Replayed);
        replayed.Resource.PlanId.Should().Be(draft.PlanId);

        var conflictingCreate = () => restartedStore.CreatePlan(
            draft,
            "owner:local-test",
            "create-alpha-0001",
            "sha256:different-payload");
        conflictingCreate.Should().Throw<TradeStoreConflictException>();

        restartedStore.ListPlanEvents(draft.PlanId)
            .Select(evt => evt.EventType)
            .Should().Equal("plan_created");
    }

    [Fact]
    public void StateChangesAttemptsAndEvents_AreDurableAndFailClosed()
    {
        using var database = new TemporaryTradeDatabase();
        var store = database.CreateStore();
        store.Initialize();
        var draft = CreateDraft("recovery");
        CreateValidatedPlan(store, draft);

        var operation = store.CreateOperation(
            "op_recovery0001",
            draft.PlanId,
            "owner:local-test",
            "enqueue-recovery-0001",
            "sha256:enqueue-recovery",
            Baseline.AddSeconds(4));
        operation.Resource.State.Should().Be(TradeOperationState.Queued);

        store.TransitionOperation(
            operation.Resource.OperationId,
            TradeOperationState.Queued,
            TradeOperationState.Running,
            TradePlanState.Queued,
            TradePlanState.Running,
            "operation_started",
            "{}",
            Baseline.AddSeconds(5));

        var itemId = draft.Items[0].ItemId;
        store.TransitionItem(
            operation.Resource.OperationId,
            TradeOperationState.Running,
            itemId,
            TradePlanItemState.Prepared,
            TradePlanItemState.Searching,
            "partner_search_started",
            "{}",
            Baseline.AddSeconds(6));
        store.StartAttempt(
            "attempt_recovery01",
            operation.Resource.OperationId,
            itemId,
            1,
            Baseline.AddSeconds(6));
        store.TransitionItem(
            operation.Resource.OperationId,
            TradeOperationState.Running,
            itemId,
            TradePlanItemState.Searching,
            TradePlanItemState.PartnerFound,
            "partner_found",
            "{}",
            Baseline.AddSeconds(7));
        store.TransitionItem(
            operation.Resource.OperationId,
            TradeOperationState.Running,
            itemId,
            TradePlanItemState.PartnerFound,
            TradePlanItemState.Offered,
            "offer_observed",
            "{}",
            Baseline.AddSeconds(8));
        store.TransitionItem(
            operation.Resource.OperationId,
            TradeOperationState.Running,
            itemId,
            TradePlanItemState.Offered,
            TradePlanItemState.Confirming,
            "confirmation_started",
            "{}",
            Baseline.AddSeconds(9));

        var unsafeRetry = () => store.TransitionItem(
            operation.Resource.OperationId,
            TradeOperationState.Running,
            itemId,
            TradePlanItemState.Confirming,
            TradePlanItemState.Pending,
            "unsafe_retry",
            "{}",
            Baseline.AddSeconds(10));
        unsafeRetry.Should().Throw<InvalidOperationException>();
        store.GetPlan(draft.PlanId)!.Items[0].State.Should().Be(TradePlanItemState.Confirming);

        var finishedAttempt = store.FinishAttempt(
            "attempt_recovery01",
            Baseline.AddSeconds(10),
            "SETTLEMENT_UNCERTAIN",
            irreversibleBoundaryCrossed: true);
        finishedAttempt.IrreversibleBoundaryCrossed.Should().BeTrue();
        finishedAttempt.FailureCode.Should().Be("SETTLEMENT_UNCERTAIN");

        store.TransitionItem(
            operation.Resource.OperationId,
            TradeOperationState.Running,
            itemId,
            TradePlanItemState.Confirming,
            TradePlanItemState.NeedsAttention,
            "settlement_uncertain",
            """{"code":"SETTLEMENT_UNCERTAIN"}""",
            Baseline.AddSeconds(11),
            lastErrorJson: """{"code":"SETTLEMENT_UNCERTAIN"}""",
            settlementEvidenceJson: """{"box_slot_changed":false}""");

        var restartedStore = database.CreateStore();
        restartedStore.Initialize();
        restartedStore.ListRecoverableOperations()
            .Should().ContainSingle(op => op.OperationId == operation.Resource.OperationId)
            .Which.State.Should().Be(TradeOperationState.NeedsAttention);
        restartedStore.GetPlan(draft.PlanId)!.State
            .Should().Be(TradePlanState.NeedsAttention);
        restartedStore.GetAttempts(itemId)
            .Should().ContainSingle()
            .Which.IrreversibleBoundaryCrossed.Should().BeTrue();
        restartedStore.GetPlan(draft.PlanId)!.Items[0].SettlementEvidenceJson
            .Should().Be("""{"box_slot_changed":false}""");

        var planEvents = restartedStore.ListPlanEvents(draft.PlanId);
        planEvents.Select(evt => evt.Sequence)
            .Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        restartedStore.ListEvents(operation.Resource.OperationId)
            .Should().OnlyContain(evt => evt.OperationId == operation.Resource.OperationId);
    }

    [Fact]
    public void TerminalOperation_IsNotReturnedForRestartRecovery()
    {
        using var database = new TemporaryTradeDatabase();
        var store = database.CreateStore();
        store.Initialize();
        var operation = CreateRunningOperation(store, "complete");
        var plan = store.GetPlan(operation.PlanId)!;

        CompleteItem(store, operation.OperationId, plan.Items[0].ItemId);
        store.TransitionItem(
            operation.OperationId,
            TradeOperationState.Running,
            plan.Items[1].ItemId,
            TradePlanItemState.Prepared,
            TradePlanItemState.Skipped,
            "item_skipped",
            """{"reason":"operator_policy"}""",
            Baseline.AddSeconds(20));

        store.TransitionOperation(
            operation.OperationId,
            TradeOperationState.Running,
            TradeOperationState.Completed,
            TradePlanState.Running,
            TradePlanState.Completed,
            "operation_completed",
            "{}",
            Baseline.AddMinutes(1));

        database.CreateStore().ListRecoverableOperations()
            .Should().NotContain(op => op.OperationId == operation.OperationId);
    }

    [Fact]
    public void PlanValidation_RequiresEveryItemToBePrepared()
    {
        using var database = new TemporaryTradeDatabase();
        var store = database.CreateStore();
        store.Initialize();
        var draft = CreateDraft("unprepared");
        store.CreatePlan(
            draft,
            "owner:local-test",
            "create-unprepared-0001",
            "sha256:create-unprepared");
        store.PrepareItem(
            draft.PlanId,
            draft.Items[0].ItemId,
            "sha256:prepared-first",
            Baseline.AddSeconds(1));

        var validate = () => store.TransitionPlan(
            draft.PlanId,
            TradePlanState.Draft,
            TradePlanState.Validated,
            "plan_validated",
            "{}",
            Baseline.AddSeconds(2),
            validationRuntimeGeneration: "runtime-lza-1");

        validate.Should().Throw<TradeStoreConflictException>();
        store.GetPlan(draft.PlanId)!.State.Should().Be(TradePlanState.Draft);
        store.ListPlanEvents(draft.PlanId)
            .Select(evt => evt.EventType)
            .Should().Equal("plan_created", "item_prepared");
    }

    [Fact]
    public void Lease_IsExclusiveAndExpiredLeaseCanBeTakenOver()
    {
        using var database = new TemporaryTradeDatabase();
        var store = database.CreateStore();
        store.Initialize();
        var firstOperation = CreateRunningOperation(store, "leasea");
        var secondOperation = CreateRunningOperation(store, "leaseb");
        var now = Baseline.AddMinutes(2);

        var first = store.TryAcquireLease(
            "bot-lza-1",
            firstOperation.OperationId,
            "owner-a-hash",
            now,
            now.AddSeconds(30));
        first.Acquired.Should().BeTrue();

        var blocked = store.TryAcquireLease(
            "bot-lza-1",
            secondOperation.OperationId,
            "owner-b-hash",
            now.AddSeconds(1),
            now.AddSeconds(31));
        blocked.Acquired.Should().BeFalse();
        blocked.Current.OperationId.Should().Be(firstOperation.OperationId);

        var takeover = store.TryAcquireLease(
            "bot-lza-1",
            secondOperation.OperationId,
            "owner-b-hash",
            now.AddSeconds(30),
            now.AddSeconds(60));
        takeover.Acquired.Should().BeTrue();
        takeover.Current.OperationId.Should().Be(secondOperation.OperationId);
        takeover.Current.Revision.Should().Be(2);

        store.RenewLease(
            "bot-lza-1",
            firstOperation.OperationId,
            "owner-a-hash",
            now.AddSeconds(31),
            now.AddSeconds(61)).Should().BeFalse();
        store.ReleaseLease(
            "bot-lza-1",
            secondOperation.OperationId,
            "owner-b-hash").Should().BeTrue();
        store.GetLease("bot-lza-1").Should().BeNull();
    }

    [Fact]
    public async Task ConcurrentLeaseAcquisition_AllowsExactlyOneOwner()
    {
        using var database = new TemporaryTradeDatabase();
        var setupStore = database.CreateStore();
        setupStore.Initialize();
        var firstOperation = CreateRunningOperation(setupStore, "racea");
        var secondOperation = CreateRunningOperation(setupStore, "raceb");
        var now = Baseline.AddMinutes(3);
        using var barrier = new Barrier(3);

        Task<TradeLeaseAcquireResult> Acquire(
            string operationId,
            string ownerHash) =>
            Task.Run(() =>
            {
                var store = database.CreateStore();
                barrier.SignalAndWait();
                return store.TryAcquireLease(
                    "bot-race-1",
                    operationId,
                    ownerHash,
                    now,
                    now.AddSeconds(30));
            });

        var firstTask = Acquire(firstOperation.OperationId, "race-owner-a");
        var secondTask = Acquire(secondOperation.OperationId, "race-owner-b");
        barrier.SignalAndWait();

        var results = await Task.WhenAll(firstTask, secondTask);

        results.Should().ContainSingle(result => result.Acquired);
        setupStore.GetLease("bot-race-1")!.OperationId
            .Should().Be(results.Single(result => result.Acquired).Current.OperationId);
    }

    private static TradeOperationSnapshot CreateRunningOperation(
        SqliteTradePlanStore store,
        string suffix)
    {
        var draft = CreateDraft(suffix);
        CreateValidatedPlan(store, draft);
        var operation = store.CreateOperation(
            $"op_{suffix}0001",
            draft.PlanId,
            "owner:local-test",
            $"enqueue-{suffix}-0001",
            $"sha256:enqueue-{suffix}",
            Baseline.AddSeconds(4));
        return store.TransitionOperation(
            operation.Resource.OperationId,
            TradeOperationState.Queued,
            TradeOperationState.Running,
            TradePlanState.Queued,
            TradePlanState.Running,
            "operation_started",
            "{}",
            Baseline.AddSeconds(5));
    }

    private static void CompleteItem(
        SqliteTradePlanStore store,
        string operationId,
        string itemId)
    {
        var transitions = new[]
        {
            (TradePlanItemState.Prepared, TradePlanItemState.Searching, "partner_search_started"),
            (TradePlanItemState.Searching, TradePlanItemState.PartnerFound, "partner_found"),
            (TradePlanItemState.PartnerFound, TradePlanItemState.Offered, "offer_observed"),
            (TradePlanItemState.Offered, TradePlanItemState.Confirming, "confirmation_started"),
            (TradePlanItemState.Confirming, TradePlanItemState.Settling, "settlement_started"),
            (TradePlanItemState.Settling, TradePlanItemState.Completed, "item_completed"),
        };

        for (int i = 0; i < transitions.Length; i++)
        {
            var (from, to, eventType) = transitions[i];
            store.TransitionItem(
                operationId,
                TradeOperationState.Running,
                itemId,
                from,
                to,
                eventType,
                "{}",
                Baseline.AddSeconds(10 + i));
        }
    }

    private static void CreateValidatedPlan(
        SqliteTradePlanStore store,
        TradePlanDraft draft)
    {
        store.CreatePlan(
            draft,
            "owner:local-test",
            $"create-{draft.PlanId}-0001",
            $"sha256:create-{draft.PlanId}");
        for (int i = 0; i < draft.Items.Count; i++)
        {
            store.PrepareItem(
                draft.PlanId,
                draft.Items[i].ItemId,
                $"sha256:prepared-{draft.Items[i].ItemId}",
                Baseline.AddSeconds(i + 1));
        }

        store.TransitionPlan(
            draft.PlanId,
            TradePlanState.Draft,
            TradePlanState.Validated,
            "plan_validated",
            """{"runtime_generation":"runtime-lza-1"}""",
            Baseline.AddSeconds(3),
            validationRuntimeGeneration: "runtime-lza-1");
    }

    private static TradePlanDraft CreateDraft(string suffix) =>
        new(
            $"plan_{suffix}0001",
            "owner:local-test",
            ProgramMode.LZA,
            """{"link_code":"13333333"}""",
            new TradePlanPolicies(),
            [
                new($"item_{suffix}0001", $"client-{suffix}-1", 0, "Raichu\nLevel: 50"),
                new($"item_{suffix}0002", $"client-{suffix}-2", 1, "Pikachu\nLevel: 50"),
            ],
            Baseline);

    private sealed class TemporaryTradeDatabase : IDisposable
    {
        private readonly string _directoryPath =
            Path.Combine(Path.GetTempPath(), $"pokebot-trade-store-tests-{Guid.NewGuid():N}");

        public TemporaryTradeDatabase()
        {
            Directory.CreateDirectory(_directoryPath);
        }

        public string DatabasePath => Path.Combine(_directoryPath, "trade-control.sqlite3");

        public SqliteTradePlanStore CreateStore() => new(DatabasePath);

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            var resolved = Path.GetFullPath(_directoryPath);
            var tempRoot = Path.GetFullPath(Path.GetTempPath());
            if (!resolved.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Refusing to remove a test directory outside the temp root.");
            if (Directory.Exists(resolved))
                Directory.Delete(resolved, recursive: true);
        }
    }
}
