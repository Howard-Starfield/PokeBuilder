using SysBot.Base;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SysBot.Pokemon;

public interface ITradeOperationIdGenerator
{
    string NewOperationId();

    string NewAttemptId();
}

public sealed class Uuid7TradeOperationIdGenerator : ITradeOperationIdGenerator
{
    public string NewOperationId() => $"op_{Guid.CreateVersion7():N}";

    public string NewAttemptId() => $"attempt_{Guid.CreateVersion7():N}";
}

/// <summary>
/// Trusted in-process queue hints supplied by non-MCP channel adapters.
/// These fields never expand the MCP tool surface or bypass plan validation.
/// </summary>
public sealed record TradeQueueSubmissionHints(
    ulong TrainerId,
    string TrainerName,
    bool IsFavored,
    string? RateLimitReservationId = null);

public sealed record TradeQueueAdmission(
    int QueuePosition,
    int BypassedCount);

/// <summary>
/// Durable application service that owns plan preparation, queue supervision,
/// recovery, and authenticated operation commands.
/// </summary>
public sealed class TradeOrchestrator :
    ITradeControlApi,
    ITradeQueueObserver,
    IDisposable
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan LeaseRenewInterval = TimeSpan.FromSeconds(30);

    private readonly ITradePlanStore _store;
    private readonly TradePlanApplicationService _plans;
    private readonly ICurrentTradeRuntime _runtime;
    private readonly ITradeQueueAdapter _queue;
    private readonly ITradeControlClock _clock;
    private readonly ITradeOperationIdGenerator _ids;
    private readonly ConcurrentDictionary<string, ActiveOperation> _active = [];
    private readonly ConcurrentDictionary<string, TradeQueueSubmissionHints>
        _submissionHints = [];
    private readonly CancellationTokenSource _shutdown = new();

    public TradeOrchestrator(
        ITradePlanStore store,
        TradePlanApplicationService plans,
        ICurrentTradeRuntime runtime,
        ITradeQueueAdapter queue,
        ITradeControlClock clock,
        ITradeOperationIdGenerator ids)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _plans = plans ?? throw new ArgumentNullException(nameof(plans));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _ids = ids ?? throw new ArgumentNullException(nameof(ids));
    }

    public TradeControlResponse<TradeRuntimeSnapshot> ListBotInstances(
        string ownerId,
        bool includeOffline)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
            return Fail<TradeRuntimeSnapshot>(
                TradeControlErrorCodes.InvalidRequest,
                "An authenticated owner is required.");

        var snapshot = _runtime.Inspect();
        if (includeOffline)
            return TradeControlResponse<TradeRuntimeSnapshot>.Ok(snapshot);
        return TradeControlResponse<TradeRuntimeSnapshot>.Ok(
            snapshot with
            {
                Bots = snapshot.Bots.Where(z => z.IsRunning).ToArray(),
            });
    }

    public TradeControlResponse<TradePlanStructuralValidation> ValidateTradePlan(
        CreateTradePlanCommand command)
    {
        try
        {
            var validation = _plans.Validate(command);
            if (!validation.IsValid)
                return TradeControlResponse<TradePlanStructuralValidation>.Ok(validation);

            var errors = ValidatePreparedItems(command);
            return TradeControlResponse<TradePlanStructuralValidation>.Ok(
                new(errors.Count == 0, errors));
        }
        catch (Exception ex)
        {
            return FromException<TradePlanStructuralValidation>(ex);
        }
    }

    public TradeControlResponse<TradePlanSnapshot> CreateTradePlan(
        CreateTradePlanCommand command)
    {
        try
        {
            var created = _plans.CreateDraft(command);
            return TradeControlResponse<TradePlanSnapshot>.Ok(created.Resource);
        }
        catch (Exception ex)
        {
            return FromException<TradePlanSnapshot>(ex);
        }
    }

    public TradeControlResponse<TradePlanSnapshot> GetTradePlan(
        string ownerId,
        string planId)
    {
        try
        {
            var plan = OwnedPlan(ownerId, planId);
            return plan is null
                ? NotFound<TradePlanSnapshot>("trade plan", planId)
                : TradeControlResponse<TradePlanSnapshot>.Ok(plan);
        }
        catch (Exception ex)
        {
            return FromException<TradePlanSnapshot>(ex);
        }
    }

    public TradeControlResponse<TradeOperationSnapshot> EnqueueTradePlan(
        string ownerId,
        string planId,
        string idempotencyKey) =>
        EnqueueTradePlanCore(ownerId, planId, idempotencyKey, null);

    /// <summary>
    /// Enqueues through the same durable dispatcher while preserving trusted
    /// channel identity, priority, and rate-limit accounting.
    /// </summary>
    public TradeControlResponse<TradeOperationSnapshot> EnqueueTradePlanWithQueueHints(
        string ownerId,
        string planId,
        string idempotencyKey,
        TradeQueueSubmissionHints hints)
    {
        ArgumentNullException.ThrowIfNull(hints);
        return EnqueueTradePlanCore(ownerId, planId, idempotencyKey, hints);
    }

    public TradeControlResponse<TradeQueueAdmission> GetQueueAdmission(
        string ownerId,
        string operationId)
    {
        var operation = OwnedOperation(ownerId, operationId);
        if (operation is null)
            return NotFound<TradeQueueAdmission>("trade operation", operationId);
        if (!_active.TryGetValue(operationId, out var active))
        {
            return Conflict<TradeQueueAdmission>(
                $"Operation '{operationId}' has not been admitted to a live queue.");
        }

        lock (active.Sync)
        {
            return active.Registration is null
                ? Conflict<TradeQueueAdmission>(
                    $"Operation '{operationId}' has not been admitted to a live queue.")
                : TradeControlResponse<TradeQueueAdmission>.Ok(new(
                    active.Registration.QueuePosition,
                    active.Registration.BypassedCount));
        }
    }

    private TradeControlResponse<TradeOperationSnapshot> EnqueueTradePlanCore(
        string ownerId,
        string planId,
        string idempotencyKey,
        TradeQueueSubmissionHints? hints)
    {
        try
        {
            if (!ValidIdempotencyKey(idempotencyKey))
                return Invalid<TradeOperationSnapshot>(
                    "idempotency_key",
                    "Idempotency key must contain between 8 and 128 characters.");

            var plan = OwnedPlan(ownerId, planId);
            if (plan is null)
                return NotFound<TradeOperationSnapshot>("trade plan", planId);

            if (plan.State == TradePlanState.Draft)
            {
                var prepared = PrepareDraft(plan);
                if (prepared.Error is not null)
                    return TradeControlResponse<TradeOperationSnapshot>.Fail(prepared.Error);
                plan = prepared.Plan!;
            }

            var operation = _store.CreateOperation(
                _ids.NewOperationId(),
                plan.PlanId,
                $"owner:{ownerId}:enqueue",
                idempotencyKey,
                Hash($"enqueue:{ownerId}:{plan.PlanId}"),
                _clock.UtcNow);

            if (hints is not null)
                _submissionHints[operation.Resource.OperationId] = hints;
            if (operation.Outcome == TradeStoreIdempotencyOutcome.Created)
                _ = Task.Run(() => DispatchQueuedOperation(operation.Resource.OperationId));
            return TradeControlResponse<TradeOperationSnapshot>.Ok(operation.Resource);
        }
        catch (Exception ex)
        {
            return FromException<TradeOperationSnapshot>(ex);
        }
    }

    public TradeControlResponse<TradeOperationSnapshot> GetTradeOperation(
        string ownerId,
        string operationId)
    {
        try
        {
            var operation = OwnedOperation(ownerId, operationId);
            return operation is null
                ? NotFound<TradeOperationSnapshot>("trade operation", operationId)
                : TradeControlResponse<TradeOperationSnapshot>.Ok(operation);
        }
        catch (Exception ex)
        {
            return FromException<TradeOperationSnapshot>(ex);
        }
    }

    public TradeControlResponse<IReadOnlyList<TradeEventSnapshot>> ListTradeEvents(
        string ownerId,
        string operationId,
        long afterSequence,
        int limit)
    {
        try
        {
            if (OwnedOperation(ownerId, operationId) is null)
                return NotFound<IReadOnlyList<TradeEventSnapshot>>(
                    "trade operation",
                    operationId);
            if (afterSequence < 0 || limit is < 1 or > 200)
            {
                return Invalid<IReadOnlyList<TradeEventSnapshot>>(
                    "pagination",
                    "after_sequence must be non-negative and limit must be from 1 through 200.");
            }
            return TradeControlResponse<IReadOnlyList<TradeEventSnapshot>>.Ok(
                _store.ListEvents(operationId, afterSequence, limit));
        }
        catch (Exception ex)
        {
            return FromException<IReadOnlyList<TradeEventSnapshot>>(ex);
        }
    }

    public TradeControlResponse<TradeOperationSnapshot> PauseTradeOperation(
        string ownerId,
        string operationId,
        string idempotencyKey,
        string reason)
    {
        try
        {
            var inputError = ValidateMutationInput(idempotencyKey, reason, false, true);
            if (inputError is not null)
                return TradeControlResponse<TradeOperationSnapshot>.Fail(inputError);
            var operation = OwnedOperation(ownerId, operationId);
            if (operation is null)
                return NotFound<TradeOperationSnapshot>("trade operation", operationId);
            ClaimOperationCommand(
                ownerId,
                operationId,
                idempotencyKey,
                $"pause:{operationId}:{RedactReason(reason)}");
            if (operation.State == TradeOperationState.Paused)
                return TradeControlResponse<TradeOperationSnapshot>.Ok(operation);
            if (operation.State is not (TradeOperationState.Queued or TradeOperationState.Running))
            {
                return Conflict<TradeOperationSnapshot>(
                    $"Operation '{operationId}' cannot be paused from {operation.State}.");
            }

            if (operation.State == TradeOperationState.Running &&
                IsIrreversible(operation))
            {
                if (_active.TryGetValue(operationId, out var active))
                    active.PendingAction = PendingOperationAction.Pause;
                return TradeControlResponse<TradeOperationSnapshot>.Ok(operation);
            }

            CancelRegistration(operationId);
            FinishOpenAttempt(operation, TradeControlErrorCodes.PartnerDisconnected, false);
            var updated = TransitionOperation(
                operation,
                TradeOperationState.Paused,
                TradePlanState.Paused,
                "pause_requested",
                new { reason = RedactReason(reason), idempotency_key_hash = Hash(idempotencyKey) });
            ReleaseActive(operationId);
            return TradeControlResponse<TradeOperationSnapshot>.Ok(updated);
        }
        catch (Exception ex)
        {
            return FromException<TradeOperationSnapshot>(ex);
        }
    }

    public TradeControlResponse<TradeOperationSnapshot> ResumeTradeOperation(
        string ownerId,
        string operationId,
        string idempotencyKey)
    {
        try
        {
            if (!ValidIdempotencyKey(idempotencyKey))
                return Invalid<TradeOperationSnapshot>(
                    "idempotency_key",
                    "Idempotency key must contain between 8 and 128 characters.");
            var operation = OwnedOperation(ownerId, operationId);
            if (operation is null)
                return NotFound<TradeOperationSnapshot>("trade operation", operationId);
            ClaimOperationCommand(
                ownerId,
                operationId,
                idempotencyKey,
                $"resume:{operationId}");
            if (operation.State is TradeOperationState.Queued or TradeOperationState.Running)
                return TradeControlResponse<TradeOperationSnapshot>.Ok(operation);
            if (operation.State != TradeOperationState.Paused)
            {
                return Conflict<TradeOperationSnapshot>(
                    $"Operation '{operationId}' cannot be resumed from {operation.State}.");
            }

            var updated = TransitionOperation(
                operation,
                TradeOperationState.Queued,
                TradePlanState.Queued,
                "resume_requested",
                new { idempotency_key_hash = Hash(idempotencyKey) });
            _ = Task.Run(() => DispatchQueuedOperation(operationId));
            return TradeControlResponse<TradeOperationSnapshot>.Ok(updated);
        }
        catch (Exception ex)
        {
            return FromException<TradeOperationSnapshot>(ex);
        }
    }

    public TradeControlResponse<TradeOperationSnapshot> CancelTradeOperation(
        string ownerId,
        string operationId,
        string idempotencyKey,
        bool confirm,
        string reason)
    {
        try
        {
            var inputError = ValidateMutationInput(
                idempotencyKey,
                reason,
                requireConfirmation: true,
                confirm);
            if (inputError is not null)
                return TradeControlResponse<TradeOperationSnapshot>.Fail(inputError);
            var operation = OwnedOperation(ownerId, operationId);
            if (operation is null)
                return NotFound<TradeOperationSnapshot>("trade operation", operationId);
            ClaimOperationCommand(
                ownerId,
                operationId,
                idempotencyKey,
                $"cancel:{operationId}:true:{RedactReason(reason)}");
            if (operation.State == TradeOperationState.Cancelled)
                return TradeControlResponse<TradeOperationSnapshot>.Ok(operation);
            if (operation.State is TradeOperationState.Completed or TradeOperationState.Failed)
            {
                return Conflict<TradeOperationSnapshot>(
                    $"Operation '{operationId}' is already terminal.");
            }

            if (operation.State == TradeOperationState.Running &&
                IsIrreversible(operation))
            {
                if (_active.TryGetValue(operationId, out var active))
                    active.PendingAction = PendingOperationAction.Cancel;
                return TradeControlResponse<TradeOperationSnapshot>.Ok(operation);
            }

            CancelRegistration(operationId);
            FinishOpenAttempt(operation, "USER_CANCELLED", false);
            var updated = TransitionOperation(
                operation,
                TradeOperationState.Cancelled,
                TradePlanState.Cancelled,
                "cancel_requested",
                new { reason = RedactReason(reason), idempotency_key_hash = Hash(idempotencyKey) });
            ReleaseActive(operationId);
            return TradeControlResponse<TradeOperationSnapshot>.Ok(updated);
        }
        catch (Exception ex)
        {
            return FromException<TradeOperationSnapshot>(ex);
        }
    }

    public TradeControlResponse<TradeOperationSnapshot> ResolveTradeAttention(
        string ownerId,
        string operationId,
        string idempotencyKey,
        string itemId,
        TradeAttentionResolution resolution,
        bool confirm,
        string reason)
    {
        try
        {
            var inputError = ValidateMutationInput(
                idempotencyKey,
                reason,
                requireConfirmation: true,
                confirm);
            if (inputError is not null)
                return TradeControlResponse<TradeOperationSnapshot>.Fail(inputError);

            var operation = OwnedOperation(ownerId, operationId);
            if (operation is null)
                return NotFound<TradeOperationSnapshot>("trade operation", operationId);
            var commandOutcome = ClaimOperationCommand(
                ownerId,
                operationId,
                idempotencyKey,
                $"attention:{operationId}:{itemId}:{resolution}:true:{RedactReason(reason)}");
            var plan = _store.GetPlan(operation.PlanId);
            var item = plan?.Items.FirstOrDefault(z => z.ItemId == itemId);
            if (commandOutcome == TradeStoreIdempotencyOutcome.Replayed &&
                (operation.State != TradeOperationState.NeedsAttention ||
                 item?.State != TradePlanItemState.NeedsAttention))
            {
                return TradeControlResponse<TradeOperationSnapshot>.Ok(operation);
            }
            if (operation.State != TradeOperationState.NeedsAttention ||
                plan?.State != TradePlanState.NeedsAttention ||
                item?.State != TradePlanItemState.NeedsAttention)
            {
                return Conflict<TradeOperationSnapshot>(
                    "The requested item is not the operation's current attention item.");
            }

            if (resolution == TradeAttentionResolution.FailPlan)
            {
                _store.TransitionItem(
                    operationId,
                    TradeOperationState.NeedsAttention,
                    itemId,
                    TradePlanItemState.NeedsAttention,
                    TradePlanItemState.Failed,
                    "attention_resolved_failed",
                    Details(new { reason = RedactReason(reason) }),
                    _clock.UtcNow);
                var failed = TransitionOperation(
                    _store.GetOperation(operationId)!,
                    TradeOperationState.Failed,
                    TradePlanState.Failed,
                    "operation_failed_by_attention_resolution",
                    new { idempotency_key_hash = Hash(idempotencyKey) });
                return TradeControlResponse<TradeOperationSnapshot>.Ok(failed);
            }

            var nextItemState = resolution switch
            {
                TradeAttentionResolution.MarkCompleted => TradePlanItemState.Completed,
                TradeAttentionResolution.RetryCurrent => TradePlanItemState.Pending,
                TradeAttentionResolution.SkipItem => TradePlanItemState.Skipped,
                _ => throw new ArgumentOutOfRangeException(nameof(resolution)),
            };
            _store.TransitionItem(
                operationId,
                TradeOperationState.NeedsAttention,
                itemId,
                TradePlanItemState.NeedsAttention,
                nextItemState,
                "attention_resolved",
                Details(new
                {
                    resolution = resolution.ToString(),
                    reason = RedactReason(reason),
                    idempotency_key_hash = Hash(idempotencyKey),
                }),
                _clock.UtcNow,
                settlementEvidenceJson: resolution == TradeAttentionResolution.MarkCompleted
                    ? Details(new { source = "explicit_operator_resolution" })
                    : null);

            var resumed = TransitionOperation(
                _store.GetOperation(operationId)!,
                TradeOperationState.Running,
                TradePlanState.Running,
                "attention_cleared",
                new { resolution = resolution.ToString() });

            if (resolution == TradeAttentionResolution.RetryCurrent)
            {
                AdvanceItem(
                    resumed.OperationId,
                    itemId,
                    TradePlanItemState.Prepared,
                    "attention_retry_prepared");
            }

            if (AllItemsFinished(operation.PlanId))
            {
                var completed = TransitionOperation(
                    _store.GetOperation(operationId)!,
                    TradeOperationState.Completed,
                    TradePlanState.Completed,
                    "operation_completed",
                    new { source = "attention_resolution" });
                return TradeControlResponse<TradeOperationSnapshot>.Ok(completed);
            }

            _ = Task.Run(() => RequeueRunningOperation(operationId, TimeSpan.Zero));
            return TradeControlResponse<TradeOperationSnapshot>.Ok(resumed);
        }
        catch (Exception ex)
        {
            return FromException<TradeOperationSnapshot>(ex);
        }
    }

    public void RecoverNonterminalOperations()
    {
        foreach (var operation in _store.ListRecoverableOperations())
        {
            try
            {
                if (operation.State == TradeOperationState.Queued)
                {
                    _ = Task.Run(() => DispatchRecoveredWhenReady(
                        operation.OperationId));
                    continue;
                }
                if (operation.State != TradeOperationState.Running)
                    continue;

                var plan = _store.GetPlan(operation.PlanId);
                var current = plan?.Items.FirstOrDefault(
                    z => z.ItemId == operation.CurrentItemId);
                if (current?.State is TradePlanItemState.Confirming or
                    TradePlanItemState.Settling)
                {
                    FinishOpenAttempt(
                        operation,
                        TradeControlErrorCodes.SettlementUncertain,
                        true);
                    _store.TransitionItem(
                        operation.OperationId,
                        TradeOperationState.Running,
                        current.ItemId,
                        current.State,
                        TradePlanItemState.NeedsAttention,
                        "restart_settlement_uncertain",
                        Details(new
                        {
                            code = TradeControlErrorCodes.SettlementUncertain,
                            message = "Process restarted after confirmation without settlement proof.",
                        }),
                        _clock.UtcNow,
                        lastErrorJson: Details(new
                        {
                            code = TradeControlErrorCodes.SettlementUncertain,
                        }));
                    continue;
                }

                FinishOpenAttempt(
                    operation,
                    TradeControlErrorCodes.TransportDisconnected,
                    false);
                NormalizeRetryableItems(operation);
                var paused = TransitionOperation(
                    _store.GetOperation(operation.OperationId)!,
                    TradeOperationState.Paused,
                    TradePlanState.Paused,
                    "restart_recovery_staged",
                    new
                    {
                        code = TradeControlErrorCodes.TransportDisconnected,
                    });
                TransitionOperation(
                    paused,
                    TradeOperationState.Queued,
                    TradePlanState.Queued,
                    "restart_recovery_queued",
                    new { source = "process_restart" });
                _ = Task.Run(() => DispatchRecoveredWhenReady(
                    operation.OperationId));
            }
            catch (Exception ex)
            {
                LogUtil.LogError(
                    $"Unable to recover trade operation {operation.OperationId}: {ex.Message}",
                    nameof(TradeOrchestrator));
            }
        }
    }

    public void OnEvent(TradeQueueEvent tradeEvent)
    {
        try
        {
            HandleQueueEvent(tradeEvent);
        }
        catch (Exception ex)
        {
            LogUtil.LogError(
                $"Control-plane event '{tradeEvent.Kind}' failed for {tradeEvent.OperationId}: {ex.Message}",
                nameof(TradeOrchestrator));
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        foreach (var operationId in _active.Keys)
            ReleaseActive(operationId);
        foreach (var hints in _submissionHints.Values)
        {
            if (hints.RateLimitReservationId is not null)
            {
                TradeRateLimitService.Instance.ReleaseReservation(
                    hints.RateLimitReservationId);
            }
        }
        _submissionHints.Clear();
        _shutdown.Dispose();
    }

    private IReadOnlyList<TradeControlError> ValidatePreparedItems(
        CreateTradePlanCommand command)
    {
        var snapshot = _runtime.Inspect();
        if (!snapshot.IsAvailable)
        {
            return
            [
                new(
                    TradeControlErrorCodes.BotOffline,
                    "No current PokeBot runtime is available."),
            ];
        }
        if (snapshot.GameMode != command.GameMode)
        {
            return
            [
                new(
                    TradeControlErrorCodes.ModeMismatch,
                    $"The current runtime is {snapshot.GameMode}, not {command.GameMode}."),
            ];
        }

        var errors = new List<TradeControlError>();
        foreach (var item in command.Items)
        {
            var prepared = _queue.Prepare(
                command.GameMode,
                snapshot.Generation,
                item.ClientItemId,
                item.ShowdownSet);
            if (prepared.Error is not null)
                errors.Add(prepared.Error);
        }
        return errors;
    }

    private (TradePlanSnapshot? Plan, TradeControlError? Error) PrepareDraft(
        TradePlanSnapshot plan)
    {
        var snapshot = _runtime.Inspect();
        if (!snapshot.IsAvailable)
        {
            return (null, new(
                TradeControlErrorCodes.BotOffline,
                "No current PokeBot runtime is available."));
        }
        if (snapshot.GameMode != plan.GameMode)
        {
            return (null, new(
                TradeControlErrorCodes.ModeMismatch,
                $"The current runtime is {snapshot.GameMode}, not {plan.GameMode}."));
        }

        foreach (var item in plan.Items.OrderBy(z => z.Position))
        {
            if (item.State == TradePlanItemState.Prepared)
                continue;
            var result = _queue.Prepare(
                plan.GameMode,
                snapshot.Generation,
                item.ItemId,
                item.ShowdownSet);
            if (result.Error is not null)
                return (null, result.Error);
            _store.PrepareItem(
                plan.PlanId,
                item.ItemId,
                result.Prepared!.PreparedHash,
                _clock.UtcNow);
        }

        return (_store.TransitionPlan(
            plan.PlanId,
            TradePlanState.Draft,
            TradePlanState.Validated,
            "plan_validated",
            Details(new
            {
                runtime_generation = snapshot.Generation,
                game_mode = snapshot.GameMode.ToString(),
            }),
            _clock.UtcNow,
            validationRuntimeGeneration: snapshot.Generation), null);
    }

    private void DispatchQueuedOperation(string operationId)
    {
        try
        {
            if (_shutdown.IsCancellationRequested)
                return;
            var operation = _store.GetOperation(operationId);
            if (operation is null || operation.State != TradeOperationState.Queued)
                return;
            var plan = _store.GetPlan(operation.PlanId);
            if (plan is null)
                return;

            var snapshot = _runtime.Inspect();
            if (!snapshot.IsAvailable || !snapshot.IsRunning ||
                snapshot.GameMode != plan.GameMode)
            {
                TransitionOperation(
                    operation,
                    TradeOperationState.Paused,
                    TradePlanState.Paused,
                    "dispatch_paused_runtime_unavailable",
                    new
                    {
                        code = snapshot.GameMode == plan.GameMode
                            ? TradeControlErrorCodes.BotOffline
                            : TradeControlErrorCodes.ModeMismatch,
                    });
                return;
            }

            var candidates = snapshot.Bots
                .Where(z => z.IsRunning)
                .OrderByDescending(z => z.CurrentRoutine.IsTradeBot())
                .ThenBy(z => z.InstanceId, StringComparer.Ordinal)
                .ToArray();
            if (candidates.Length == 0)
            {
                TransitionOperation(
                    operation,
                    TradeOperationState.Paused,
                    TradePlanState.Paused,
                    "dispatch_paused_no_bot",
                    new { code = TradeControlErrorCodes.BotOffline });
                return;
            }

            var leaseHash = Hash($"lease:{plan.OwnerId}");
            TradeBotInstanceSnapshot? bot = null;
            foreach (var candidate in candidates)
            {
                var now = _clock.UtcNow;
                var lease = _store.TryAcquireLease(
                    candidate.InstanceId,
                    operationId,
                    leaseHash,
                    now,
                    now.Add(LeaseDuration));
                if (lease.Acquired)
                {
                    bot = candidate;
                    break;
                }
            }
            if (bot is null)
            {
                TransitionOperation(
                    operation,
                    TradeOperationState.Paused,
                    TradePlanState.Paused,
                    "dispatch_paused_bot_busy",
                    new
                    {
                        code = TradeControlErrorCodes.BotBusy,
                        candidate_count = candidates.Length,
                    });
                return;
            }

            var active = new ActiveOperation(
                operationId,
                plan.OwnerId,
                bot.InstanceId,
                leaseHash,
                snapshot.Generation);
            if (!_active.TryAdd(operationId, active))
            {
                _store.ReleaseLease(bot.InstanceId, operationId, leaseHash);
                return;
            }

            TransitionOperation(
                operation,
                TradeOperationState.Running,
                TradePlanState.Running,
                "dispatch_started",
                new
                {
                    bot_instance_id = bot.InstanceId,
                    runtime_generation = snapshot.Generation,
                });
            active.RenewalTask = Task.Run(
                () => RenewLease(active),
                active.Cancellation.Token);
            RequeueRunningOperation(operationId, TimeSpan.Zero);
        }
        catch (Exception ex)
        {
            LogUtil.LogError(
                $"Unable to dispatch trade operation {operationId}: {ex.Message}",
                nameof(TradeOrchestrator));
            ReleaseActive(operationId);
        }
    }

    private async Task DispatchRecoveredWhenReady(string operationId)
    {
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                var operation = _store.GetOperation(operationId);
                if (operation?.State != TradeOperationState.Queued)
                    return;
                var plan = _store.GetPlan(operation.PlanId);
                if (plan is null)
                    return;
                var snapshot = _runtime.Inspect();
                if (snapshot.IsAvailable && snapshot.IsRunning)
                {
                    DispatchQueuedOperation(operationId);
                    return;
                }

                await Task.Delay(
                    TimeSpan.FromSeconds(1),
                    _shutdown.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            LogUtil.LogError(
                $"Unable to stage recovered trade operation {operationId}: {ex.Message}",
                nameof(TradeOrchestrator));
        }
    }

    private void RequeueRunningOperation(
        string operationId,
        TimeSpan delay)
    {
        if (_shutdown.IsCancellationRequested)
            return;
        if (delay > TimeSpan.Zero)
        {
            try
            {
                Task.Delay(delay, _shutdown.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        var operation = _store.GetOperation(operationId);
        var plan = operation is null ? null : _store.GetPlan(operation.PlanId);
        if (operation?.State != TradeOperationState.Running || plan is null)
            return;

        var snapshot = _runtime.Inspect();
        if (!snapshot.IsAvailable || !snapshot.IsRunning ||
            snapshot.GameMode != plan.GameMode)
        {
            TransitionOperation(
                operation,
                TradeOperationState.Paused,
                TradePlanState.Paused,
                "operation_paused_runtime_changed",
                new
                {
                    code = snapshot.GameMode == plan.GameMode
                        ? TradeControlErrorCodes.BotOffline
                        : TradeControlErrorCodes.ModeMismatch,
                });
            ReleaseActive(operationId);
            return;
        }

        NormalizeRetryableItems(operation);
        plan = _store.GetPlan(operation.PlanId)!;
        var remaining = plan.Items
            .Where(z => z.State is not (
                TradePlanItemState.Completed or
                TradePlanItemState.Skipped or
                TradePlanItemState.Failed))
            .OrderBy(z => z.Position)
            .ToArray();
        if (remaining.Length == 0)
        {
            CompleteOperation(operationId, "no_remaining_items");
            return;
        }

        var artifacts = new List<PreparedTradeItem>(remaining.Length);
        foreach (var item in remaining)
        {
            var prepared = _queue.Prepare(
                plan.GameMode,
                snapshot.Generation,
                item.ItemId,
                item.ShowdownSet);
            if (prepared.Error is not null)
            {
                TransitionOperation(
                    operation,
                    TradeOperationState.Paused,
                    TradePlanState.Paused,
                    "operation_paused_preparation_failed",
                    new { error = prepared.Error });
                ReleaseActive(operationId);
                return;
            }
            artifacts.Add(prepared.Prepared!);
        }

        var hints = _submissionHints.TryGetValue(operationId, out var configured)
            ? configured
            : DeriveQueueHints(plan.OwnerId);
        var result = _queue.Enqueue(
            new(
                operationId,
                plan.OwnerId,
                hints?.TrainerName ?? "PokeBot MCP",
                plan.GameMode,
                snapshot.Generation,
                plan.AccessJson,
                plan.Policies.Evolution,
                artifacts,
                hints?.TrainerId,
                hints?.IsFavored ?? false,
                hints?.RateLimitReservationId),
            this);
        if (result.Error is not null)
        {
            TransitionOperation(
                operation,
                TradeOperationState.Paused,
                TradePlanState.Paused,
                "operation_paused_queue_rejected",
                new { error = result.Error });
            ReleaseActive(operationId);
            return;
        }

        if (_active.TryGetValue(operationId, out var active))
        {
            lock (active.Sync)
                active.Registration = result.Registration;
        }
    }

    private void HandleQueueEvent(TradeQueueEvent tradeEvent)
    {
        if (!_active.TryGetValue(tradeEvent.OperationId, out var active))
            return;
        lock (active.Sync)
        {
            var operation = _store.GetOperation(tradeEvent.OperationId);
            if (operation?.State != TradeOperationState.Running)
                return;
            var plan = _store.GetPlan(operation.PlanId)!;

            switch (tradeEvent.Kind)
            {
                case TradeQueueEventKind.Initialized:
                case TradeQueueEventKind.Searching:
                    StartSearching(operation, tradeEvent.ItemId ?? FirstRemaining(plan)?.ItemId);
                    break;
                case TradeQueueEventKind.BatchProgress:
                    HandleBatchProgress(operation, plan, tradeEvent.BatchPosition ?? 1, active);
                    break;
                case TradeQueueEventKind.Confirming:
                    if (tradeEvent.ItemId is not null)
                        AdvanceItem(
                            operation.OperationId,
                            tradeEvent.ItemId,
                            TradePlanItemState.Confirming,
                            "item_confirming");
                    break;
                case TradeQueueEventKind.Settling:
                    if (tradeEvent.ItemId is not null)
                        AdvanceItem(
                            operation.OperationId,
                            tradeEvent.ItemId,
                            TradePlanItemState.Settling,
                            "item_settling");
                    break;
                case TradeQueueEventKind.Completed:
                    if (tradeEvent.ItemId is not null)
                        CompleteItem(
                            operation,
                            tradeEvent.ItemId,
                            "notifier_trade_finished");
                    if (AllItemsFinished(operation.PlanId))
                        CompleteOperation(operation.OperationId, "notifier_trade_finished");
                    break;
                case TradeQueueEventKind.Cancelled:
                    HandleCancellation(
                        operation,
                        plan,
                        tradeEvent.ItemId ?? operation.CurrentItemId,
                        tradeEvent.ResultCode);
                    break;
                case TradeQueueEventKind.Message:
                    break;
            }
        }
    }

    private void HandleBatchProgress(
        TradeOperationSnapshot operation,
        TradePlanSnapshot plan,
        int oneBasedPosition,
        ActiveOperation active)
    {
        var ordered = plan.Items.OrderBy(z => z.Position).ToArray();
        var currentIndex = Math.Clamp(oneBasedPosition - 1, 0, ordered.Length - 1);
        if (currentIndex > 0)
        {
            var previous = ordered[currentIndex - 1];
            if (previous.State is not (
                TradePlanItemState.Completed or
                TradePlanItemState.Skipped or
                TradePlanItemState.Failed))
            {
                CompleteItem(
                    operation,
                    previous.ItemId,
                    "next_batch_item_presented");
            }

            if (active.PendingAction != PendingOperationAction.None)
            {
                active.Registration?.RequestCancellation();
                ApplyPendingAction(operation.OperationId, active.PendingAction);
                return;
            }
        }
        StartSearching(operation, ordered[currentIndex].ItemId);
    }

    private void StartSearching(
        TradeOperationSnapshot operation,
        string? itemId)
    {
        if (itemId is null)
            return;
        var plan = _store.GetPlan(operation.PlanId)!;
        var item = plan.Items.First(z => z.ItemId == itemId);
        if (item.State == TradePlanItemState.Prepared)
        {
            _store.TransitionItem(
                operation.OperationId,
                TradeOperationState.Running,
                itemId,
                TradePlanItemState.Prepared,
                TradePlanItemState.Searching,
                "item_searching",
                "{}",
                _clock.UtcNow);
        }

        StartAttemptIfNeeded(operation.OperationId, itemId);
    }

    private void StartAttemptIfNeeded(string operationId, string itemId)
    {
        var attempts = _store.GetAttempts(itemId);
        if (attempts.LastOrDefault()?.EndedAt is null &&
            attempts.Count > 0)
            return;
        var item = _store.GetPlan(_store.GetOperation(operationId)!.PlanId)!
            .Items.First(z => z.ItemId == itemId);
        if (item.State != TradePlanItemState.Searching)
            return;
        _store.StartAttempt(
            _ids.NewAttemptId(),
            operationId,
            itemId,
            item.AttemptCount + 1,
            _clock.UtcNow);
    }

    private void CompleteItem(
        TradeOperationSnapshot operation,
        string itemId,
        string evidenceSource)
    {
        var before = _store.GetPlan(operation.PlanId)!.Items
            .First(z => z.ItemId == itemId);
        AdvanceItem(
            operation.OperationId,
            itemId,
            TradePlanItemState.Completed,
            "item_completed",
            Details(new { source = evidenceSource }));
        if (before.State != TradePlanItemState.Completed &&
            _submissionHints.TryGetValue(
                operation.OperationId,
                out var hints) &&
            hints.RateLimitReservationId is not null)
        {
            TradeRateLimitService.Instance.ConsumeReservation(
                hints.RateLimitReservationId,
                1);
        }
        FinishOpenAttempt(operation with { CurrentItemId = itemId }, null, true);
    }

    private void HandleCancellation(
        TradeOperationSnapshot operation,
        TradePlanSnapshot plan,
        string? itemId,
        string? resultCode)
    {
        if (itemId is null)
        {
            PauseForRetryExhaustion(operation, plan.Policies, resultCode);
            return;
        }
        var item = _store.GetPlan(plan.PlanId)!.Items.First(z => z.ItemId == itemId);
        var irreversible = item.State is
            TradePlanItemState.Confirming or
            TradePlanItemState.Settling;
        var failureCode = ClassifyFailure(resultCode);
        FinishOpenAttempt(operation with { CurrentItemId = itemId }, failureCode, irreversible);

        if (irreversible)
        {
            _store.TransitionItem(
                operation.OperationId,
                TradeOperationState.Running,
                itemId,
                item.State,
                TradePlanItemState.NeedsAttention,
                "settlement_uncertain",
                Details(new
                {
                    code = TradeControlErrorCodes.SettlementUncertain,
                    result_code = resultCode,
                }),
                _clock.UtcNow,
                lastErrorJson: Details(new
                {
                    code = TradeControlErrorCodes.SettlementUncertain,
                }));
            ReleaseActive(operation.OperationId);
            return;
        }

        if (item.AttemptCount <= plan.Policies.PartnerDisconnectMaxAttempts)
        {
            NormalizeRetryableItem(operation, item);
            var delay = ReconnectDelay(plan.Policies, item.AttemptCount);
            _ = Task.Run(() => RequeueRunningOperation(
                operation.OperationId,
                TimeSpan.FromMilliseconds(delay)));
            return;
        }
        PauseForRetryExhaustion(operation, plan.Policies, failureCode);
    }

    private void PauseForRetryExhaustion(
        TradeOperationSnapshot operation,
        TradePlanPolicies policies,
        string? failureCode)
    {
        if (policies.OnRetryExhausted == TradeRetryExhaustedPolicy.CancelPlan)
        {
            TransitionOperation(
                operation,
                TradeOperationState.Cancelled,
                TradePlanState.Cancelled,
                "retry_exhausted_cancelled",
                new { code = failureCode });
        }
        else if (policies.OnRetryExhausted == TradeRetryExhaustedPolicy.SkipItem &&
                 operation.CurrentItemId is not null)
        {
            var plan = _store.GetPlan(operation.PlanId)!;
            var item = plan.Items.First(z => z.ItemId == operation.CurrentItemId);
            if (item.State is not (
                TradePlanItemState.Completed or
                TradePlanItemState.Skipped or
                TradePlanItemState.Failed))
            {
                NormalizeRetryableItem(operation, item);
                item = _store.GetPlan(operation.PlanId)!.Items
                    .First(z => z.ItemId == operation.CurrentItemId);
                _store.TransitionItem(
                    operation.OperationId,
                    TradeOperationState.Running,
                    item.ItemId,
                    item.State,
                    TradePlanItemState.Skipped,
                    "retry_exhausted_item_skipped",
                    Details(new { code = failureCode }),
                    _clock.UtcNow);
            }
            _ = Task.Run(() => RequeueRunningOperation(
                operation.OperationId,
                TimeSpan.Zero));
            return;
        }
        else
        {
            TransitionOperation(
                operation,
                TradeOperationState.Paused,
                TradePlanState.Paused,
                "retry_exhausted_paused",
                new { code = failureCode });
        }
        ReleaseActive(operation.OperationId);
    }

    private void AdvanceItem(
        string operationId,
        string itemId,
        TradePlanItemState target,
        string eventType,
        string? finalEvidenceJson = null)
    {
        while (true)
        {
            var operation = _store.GetOperation(operationId)
                ?? throw new TradeStoreNotFoundException(
                    $"Trade operation '{operationId}' was not found.");
            if (operation.State != TradeOperationState.Running)
                return;
            var item = _store.GetPlan(operation.PlanId)!.Items
                .First(z => z.ItemId == itemId);
            if (item.State == target || IsBeyond(item.State, target))
                return;
            var next = NextState(item.State, target);
            _store.TransitionItem(
                operationId,
                TradeOperationState.Running,
                itemId,
                item.State,
                next,
                next == target ? eventType : EventFor(next),
                next == target && finalEvidenceJson is not null
                    ? finalEvidenceJson
                    : "{}",
                _clock.UtcNow,
                settlementEvidenceJson:
                    next == TradePlanItemState.Completed
                        ? finalEvidenceJson
                        : null);
        }
    }

    private static TradePlanItemState NextState(
        TradePlanItemState current,
        TradePlanItemState target)
    {
        if (target == TradePlanItemState.Prepared &&
            current == TradePlanItemState.Pending)
            return TradePlanItemState.Prepared;
        return current switch
        {
            TradePlanItemState.Pending => TradePlanItemState.Prepared,
            TradePlanItemState.Prepared => TradePlanItemState.Searching,
            TradePlanItemState.Searching => TradePlanItemState.PartnerFound,
            TradePlanItemState.PartnerFound => TradePlanItemState.Offered,
            TradePlanItemState.Offered => TradePlanItemState.Confirming,
            TradePlanItemState.Confirming => TradePlanItemState.Settling,
            TradePlanItemState.Settling => TradePlanItemState.Completed,
            _ => throw new TradeStoreConflictException(
                $"Cannot advance item from {current} to {target}."),
        };
    }

    private static bool IsBeyond(
        TradePlanItemState current,
        TradePlanItemState target)
    {
        var order = new Dictionary<TradePlanItemState, int>
        {
            [TradePlanItemState.Pending] = 0,
            [TradePlanItemState.Prepared] = 1,
            [TradePlanItemState.Searching] = 2,
            [TradePlanItemState.PartnerFound] = 3,
            [TradePlanItemState.Offered] = 4,
            [TradePlanItemState.Confirming] = 5,
            [TradePlanItemState.Settling] = 6,
            [TradePlanItemState.Completed] = 7,
        };
        return order.TryGetValue(current, out var currentOrder) &&
            order.TryGetValue(target, out var targetOrder) &&
            currentOrder > targetOrder;
    }

    private static string EventFor(TradePlanItemState state) =>
        state switch
        {
            TradePlanItemState.Prepared => "item_prepared_for_retry",
            TradePlanItemState.Searching => "item_searching",
            TradePlanItemState.PartnerFound => "item_partner_found",
            TradePlanItemState.Offered => "item_offered",
            TradePlanItemState.Confirming => "item_confirming",
            TradePlanItemState.Settling => "item_settling",
            TradePlanItemState.Completed => "item_completed",
            _ => "item_state_changed",
        };

    private void NormalizeRetryableItems(TradeOperationSnapshot operation)
    {
        var plan = _store.GetPlan(operation.PlanId)!;
        foreach (var item in plan.Items)
        {
            if (item.State is
                TradePlanItemState.Searching or
                TradePlanItemState.PartnerFound or
                TradePlanItemState.Offered or
                TradePlanItemState.Pending)
            {
                NormalizeRetryableItem(operation, item);
            }
        }
    }

    private void NormalizeRetryableItem(
        TradeOperationSnapshot operation,
        TradePlanItemSnapshot item)
    {
        var current = item;
        if (current.State is
            TradePlanItemState.Searching or
            TradePlanItemState.PartnerFound or
            TradePlanItemState.Offered)
        {
            current = _store.TransitionItem(
                operation.OperationId,
                TradeOperationState.Running,
                current.ItemId,
                current.State,
                TradePlanItemState.Pending,
                "item_reset_before_retry",
                "{}",
                _clock.UtcNow);
        }
        if (current.State == TradePlanItemState.Pending)
        {
            _store.TransitionItem(
                operation.OperationId,
                TradeOperationState.Running,
                current.ItemId,
                TradePlanItemState.Pending,
                TradePlanItemState.Prepared,
                "item_prepared_for_retry",
                "{}",
                _clock.UtcNow);
        }
    }

    private bool IsIrreversible(TradeOperationSnapshot operation)
    {
        if (operation.CurrentItemId is null)
            return false;
        var item = _store.GetPlan(operation.PlanId)?.Items
            .FirstOrDefault(z => z.ItemId == operation.CurrentItemId);
        return item?.State is
            TradePlanItemState.Confirming or
            TradePlanItemState.Settling;
    }

    private void FinishOpenAttempt(
        TradeOperationSnapshot operation,
        string? failureCode,
        bool irreversible)
    {
        if (operation.CurrentItemId is null)
            return;
        var attempt = _store.GetAttempts(operation.CurrentItemId)
            .LastOrDefault(z => z.EndedAt is null);
        if (attempt is null)
            return;
        _store.FinishAttempt(
            attempt.AttemptId,
            _clock.UtcNow,
            failureCode,
            irreversible);
    }

    private void CompleteOperation(string operationId, string source)
    {
        var operation = _store.GetOperation(operationId);
        if (operation?.State != TradeOperationState.Running)
            return;
        TransitionOperation(
            operation,
            TradeOperationState.Completed,
            TradePlanState.Completed,
            "operation_completed",
            new { source });
        ReleaseActive(operationId);
    }

    private bool AllItemsFinished(string planId) =>
        _store.GetPlan(planId)!.Items.All(z =>
            z.State is TradePlanItemState.Completed or TradePlanItemState.Skipped);

    private void ApplyPendingAction(
        string operationId,
        PendingOperationAction action)
    {
        var operation = _store.GetOperation(operationId);
        if (operation?.State != TradeOperationState.Running)
            return;
        if (action == PendingOperationAction.Cancel)
        {
            TransitionOperation(
                operation,
                TradeOperationState.Cancelled,
                TradePlanState.Cancelled,
                "deferred_cancel_applied",
                new { boundary = "between_items" });
        }
        else
        {
            TransitionOperation(
                operation,
                TradeOperationState.Paused,
                TradePlanState.Paused,
                "deferred_pause_applied",
                new { boundary = "between_items" });
        }
        ReleaseActive(operationId);
    }

    private TradeOperationSnapshot TransitionOperation(
        TradeOperationSnapshot operation,
        TradeOperationState nextOperation,
        TradePlanState nextPlan,
        string eventType,
        object details)
    {
        var plan = _store.GetPlan(operation.PlanId)
            ?? throw new TradeStoreNotFoundException(
                $"Trade plan '{operation.PlanId}' was not found.");
        var updated = _store.TransitionOperation(
            operation.OperationId,
            operation.State,
            nextOperation,
            plan.State,
            nextPlan,
            eventType,
            Details(details),
            _clock.UtcNow);
        if (updated.State is
            TradeOperationState.Completed or
            TradeOperationState.Failed or
            TradeOperationState.Cancelled)
        {
            FinalizeSubmission(updated.OperationId);
        }
        return updated;
    }

    private async Task RenewLease(ActiveOperation active)
    {
        try
        {
            while (!active.Cancellation.IsCancellationRequested &&
                   !_shutdown.IsCancellationRequested)
            {
                await Task.Delay(
                    LeaseRenewInterval,
                    active.Cancellation.Token).ConfigureAwait(false);
                var now = _clock.UtcNow;
                if (!_store.RenewLease(
                    active.BotInstanceId,
                    active.OperationId,
                    active.LeaseOwnerHash,
                    now,
                    now.Add(LeaseDuration)))
                {
                    LogUtil.LogError(
                        $"Lost orchestration lease for {active.OperationId}.",
                        nameof(TradeOrchestrator));
                    active.Registration?.RequestCancellation();
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CancelRegistration(string operationId)
    {
        if (_active.TryGetValue(operationId, out var active))
        {
            lock (active.Sync)
                active.Registration?.RequestCancellation();
        }
    }

    private void ReleaseActive(string operationId)
    {
        if (!_active.TryRemove(operationId, out var active))
            return;
        active.Cancellation.Cancel();
        _store.ReleaseLease(
            active.BotInstanceId,
            operationId,
            active.LeaseOwnerHash);
        active.Cancellation.Dispose();
    }

    private void FinalizeSubmission(string operationId)
    {
        if (!_submissionHints.TryRemove(operationId, out var hints) ||
            hints.RateLimitReservationId is null)
        {
            return;
        }
        TradeRateLimitService.Instance.ReleaseReservation(
            hints.RateLimitReservationId);
    }

    private static TradeQueueSubmissionHints? DeriveQueueHints(string ownerId)
    {
        const string prefix = "website:";
        if (!ownerId.StartsWith(prefix, StringComparison.Ordinal))
            return null;
        var parts = ownerId[prefix.Length..].Split(':', 2);
        if (!ulong.TryParse(parts[0], out var trainerId))
            return null;
        var favored = parts.Length == 2 &&
            string.Equals(parts[1], "favored", StringComparison.Ordinal);
        return new(
            trainerId,
            "Website trade",
            favored);
    }

    private TradePlanSnapshot? OwnedPlan(string ownerId, string planId)
    {
        if (string.IsNullOrWhiteSpace(ownerId) ||
            string.IsNullOrWhiteSpace(planId))
            return null;
        var plan = _store.GetPlan(planId);
        return plan is not null &&
            string.Equals(plan.OwnerId, ownerId, StringComparison.Ordinal)
                ? plan
                : null;
    }

    private TradeOperationSnapshot? OwnedOperation(
        string ownerId,
        string operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId))
            return null;
        var operation = _store.GetOperation(operationId);
        return operation is not null &&
            OwnedPlan(ownerId, operation.PlanId) is not null
                ? operation
                : null;
    }

    private TradeStoreIdempotencyOutcome ClaimOperationCommand(
        string ownerId,
        string operationId,
        string idempotencyKey,
        string canonicalRequest) =>
        _store.ClaimOperationCommand(
            operationId,
            $"owner:{ownerId}:operation-command",
            idempotencyKey,
            Hash(canonicalRequest),
            _clock.UtcNow);

    private static TradeControlError? ValidateMutationInput(
        string idempotencyKey,
        string reason,
        bool requireConfirmation,
        bool confirm)
    {
        if (!ValidIdempotencyKey(idempotencyKey))
        {
            return new(
                TradeControlErrorCodes.InvalidRequest,
                "Idempotency key must contain between 8 and 128 characters.",
                new Dictionary<string, object?> { ["field"] = "idempotency_key" });
        }
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
        {
            return new(
                TradeControlErrorCodes.InvalidRequest,
                "Reason must contain between 1 and 500 characters.",
                new Dictionary<string, object?> { ["field"] = "reason" });
        }
        if (requireConfirmation && !confirm)
        {
            return new(
                TradeControlErrorCodes.ConfirmationRequired,
                "Explicit confirmation is required.",
                new Dictionary<string, object?> { ["field"] = "confirm" });
        }
        return null;
    }

    private static bool ValidIdempotencyKey(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length is >= 8 and <= 128;

    private static int ReconnectDelay(
        TradePlanPolicies policies,
        int attemptCount)
    {
        var index = Math.Clamp(
            attemptCount,
            0,
            policies.TransportReconnectDelaysMs.Count - 1);
        return policies.TransportReconnectDelaysMs[index];
    }

    private static string ClassifyFailure(string? resultCode) =>
        Enum.TryParse<PokeTradeResult>(resultCode, out var result) &&
        result == PokeTradeResult.ExceptionConnection
            ? TradeControlErrorCodes.TransportDisconnected
            : TradeControlErrorCodes.PartnerDisconnected;

    private static TradePlanItemSnapshot? FirstRemaining(TradePlanSnapshot plan) =>
        plan.Items
            .OrderBy(z => z.Position)
            .FirstOrDefault(z => z.State is not (
                TradePlanItemState.Completed or
                TradePlanItemState.Skipped or
                TradePlanItemState.Failed));

    private static string RedactReason(string reason) =>
        reason.Trim().Length <= 500 ? reason.Trim() : reason.Trim()[..500];

    private static string Hash(string value) =>
        Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value)))
        .ToLowerInvariant();

    private static string Details(object value) =>
        JsonSerializer.Serialize(value);

    private static TradeControlResponse<T> Invalid<T>(
        string field,
        string message) =>
        TradeControlResponse<T>.Fail(new(
            TradeControlErrorCodes.InvalidRequest,
            message,
            new Dictionary<string, object?> { ["field"] = field }));

    private static TradeControlResponse<T> Conflict<T>(string message) =>
        TradeControlResponse<T>.Fail(new(
            TradeControlErrorCodes.PlanConflict,
            message));

    private static TradeControlResponse<T> NotFound<T>(
        string resource,
        string id) =>
        TradeControlResponse<T>.Fail(new(
            TradeControlErrorCodes.InvalidRequest,
            $"The requested {resource} was not found.",
            new Dictionary<string, object?> { ["resource_id"] = id }));

    private static TradeControlResponse<T> Fail<T>(
        string code,
        string message) =>
        TradeControlResponse<T>.Fail(new(code, message));

    private static TradeControlResponse<T> FromException<T>(Exception ex) =>
        ex switch
        {
            TradePlanValidationException validation =>
                TradeControlResponse<T>.Fail(validation.Errors.First()),
            TradeStoreConflictException or TradeStoreConcurrencyException =>
                Conflict<T>(ex.Message),
            TradeStoreNotFoundException =>
                TradeControlResponse<T>.Fail(new(
                    TradeControlErrorCodes.InvalidRequest,
                    ex.Message)),
            _ => TradeControlResponse<T>.Fail(new(
                TradeControlErrorCodes.PlanConflict,
                "The trade control operation could not be completed.",
                new Dictionary<string, object?>
                {
                    ["reason"] = ex.GetType().Name,
                })),
        };

    private enum PendingOperationAction
    {
        None,
        Pause,
        Cancel,
    }

    private sealed class ActiveOperation
    {
        public ActiveOperation(
            string operationId,
            string ownerId,
            string botInstanceId,
            string leaseOwnerHash,
            string runtimeGeneration)
        {
            OperationId = operationId;
            OwnerId = ownerId;
            BotInstanceId = botInstanceId;
            LeaseOwnerHash = leaseOwnerHash;
            RuntimeGeneration = runtimeGeneration;
        }

        public object Sync { get; } = new();

        public string OperationId { get; }

        public string OwnerId { get; }

        public string BotInstanceId { get; }

        public string LeaseOwnerHash { get; }

        public string RuntimeGeneration { get; }

        public CancellationTokenSource Cancellation { get; } = new();

        public ITradeQueueRegistration? Registration { get; set; }

        public PendingOperationAction PendingAction { get; set; }

        public Task? RenewalTask { get; set; }
    }
}
