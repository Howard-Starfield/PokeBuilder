using System.Collections.Generic;
using System.Linq;

namespace SysBot.Pokemon;

/// <summary>
/// Durable lifecycle states for a multi-Pokémon trade plan.
/// </summary>
public enum TradePlanState
{
    Draft,
    Validated,
    Queued,
    Running,
    Paused,
    NeedsAttention,
    Completed,
    Failed,
    Cancelled,
}

/// <summary>
/// Durable lifecycle states for one item in a trade plan.
/// </summary>
public enum TradePlanItemState
{
    Pending,
    Prepared,
    Searching,
    PartnerFound,
    Offered,
    Confirming,
    Settling,
    NeedsAttention,
    Completed,
    Skipped,
    Failed,
}

public enum TradeOperationState
{
    Queued,
    Running,
    Paused,
    NeedsAttention,
    Completed,
    Failed,
    Cancelled,
}

public enum TradeEvolutionPolicy
{
    Block,
    AllowManual,
    AllowAndHandle,
}

public enum TradeRetryExhaustedPolicy
{
    Pause,
    SkipItem,
    CancelPlan,
}

public enum TradeUncertainSettlementPolicy
{
    NeedsAttention,
}

/// <summary>
/// Conservative defaults for control-plane initiated trades.
/// </summary>
public sealed record TradePlanPolicies
{
    public TradeEvolutionPolicy Evolution { get; init; } = TradeEvolutionPolicy.Block;

    public int PartnerDisconnectMaxAttempts { get; init; } = 3;

    public IReadOnlyList<int> TransportReconnectDelaysMs { get; init; } =
        [0, 250, 1_000, 5_000, 30_000];

    public TradeRetryExhaustedPolicy OnRetryExhausted { get; init; } =
        TradeRetryExhaustedPolicy.Pause;

    public TradeUncertainSettlementPolicy OnUncertainSettlement { get; init; } =
        TradeUncertainSettlementPolicy.NeedsAttention;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (PartnerDisconnectMaxAttempts is < 0 or > 20)
            errors.Add($"{nameof(PartnerDisconnectMaxAttempts)} must be between 0 and 20.");

        if (TransportReconnectDelaysMs.Count == 0)
            errors.Add($"{nameof(TransportReconnectDelaysMs)} must contain at least one delay.");
        else if (TransportReconnectDelaysMs.Count > 20)
            errors.Add($"{nameof(TransportReconnectDelaysMs)} cannot contain more than 20 delays.");
        else if (TransportReconnectDelaysMs.Any(delay => delay is < 0 or > 300_000))
            errors.Add($"{nameof(TransportReconnectDelaysMs)} delays must be between 0 and 300000 milliseconds.");

        return errors;
    }
}

/// <summary>
/// Stable domain error shape shared by HTTP and MCP adapters.
/// </summary>
public sealed record TradeControlError(
    string Code,
    string Message,
    IReadOnlyDictionary<string, object?>? Details = null);

public static class TradeControlErrorCodes
{
    public const string InvalidRequest = "INVALID_REQUEST";
    public const string BotOffline = "BOT_OFFLINE";
    public const string BotBusy = "BOT_BUSY";
    public const string ModeMismatch = "MODE_MISMATCH";
    public const string QueueClosed = "QUEUE_CLOSED";
    public const string LegalityFailed = "LEGALITY_FAILED";
    public const string ItemBlocked = "ITEM_BLOCKED";
    public const string PlanConflict = "PLAN_CONFLICT";
    public const string PartnerDisconnected = "PARTNER_DISCONNECTED";
    public const string TransportDisconnected = "TRANSPORT_DISCONNECTED";
    public const string SettlementUncertain = "SETTLEMENT_UNCERTAIN";
    public const string EvolutionBlocked = "EVOLUTION_BLOCKED";
    public const string EvolutionRequiresAttention = "EVOLUTION_REQUIRES_ATTENTION";
    public const string RateLimited = "RATE_LIMITED";
    public const string ConfirmationRequired = "CONFIRMATION_REQUIRED";
}

public static class TradeControlStateTransitions
{
    public static bool CanTransitionTo(this TradePlanState current, TradePlanState next) =>
        current switch
        {
            TradePlanState.Draft => next is TradePlanState.Validated or TradePlanState.Cancelled,
            TradePlanState.Validated => next is TradePlanState.Draft or TradePlanState.Queued or TradePlanState.Cancelled,
            TradePlanState.Queued => next is TradePlanState.Running or TradePlanState.Paused or TradePlanState.Failed or TradePlanState.Cancelled,
            TradePlanState.Running => next is TradePlanState.Paused or TradePlanState.NeedsAttention or TradePlanState.Completed or TradePlanState.Failed or TradePlanState.Cancelled,
            TradePlanState.Paused => next is TradePlanState.Queued or TradePlanState.Running or TradePlanState.Failed or TradePlanState.Cancelled,
            TradePlanState.NeedsAttention => next is TradePlanState.Running or TradePlanState.Paused or TradePlanState.Failed or TradePlanState.Cancelled,
            TradePlanState.Completed or TradePlanState.Failed or TradePlanState.Cancelled => false,
            _ => false,
        };

    public static bool CanTransitionTo(this TradePlanItemState current, TradePlanItemState next) =>
        current switch
        {
            TradePlanItemState.Pending => next is TradePlanItemState.Prepared or TradePlanItemState.Skipped or TradePlanItemState.Failed,
            TradePlanItemState.Prepared => next is TradePlanItemState.Pending or TradePlanItemState.Searching or TradePlanItemState.Skipped or TradePlanItemState.Failed,
            TradePlanItemState.Searching => next is TradePlanItemState.Pending or TradePlanItemState.PartnerFound or TradePlanItemState.Skipped or TradePlanItemState.Failed,
            TradePlanItemState.PartnerFound => next is TradePlanItemState.Pending or TradePlanItemState.Searching or TradePlanItemState.Offered or TradePlanItemState.Skipped or TradePlanItemState.Failed,
            TradePlanItemState.Offered => next is TradePlanItemState.Pending or TradePlanItemState.Searching or TradePlanItemState.Confirming or TradePlanItemState.Skipped or TradePlanItemState.Failed,
            TradePlanItemState.Confirming => next is TradePlanItemState.Settling or TradePlanItemState.NeedsAttention or TradePlanItemState.Failed,
            TradePlanItemState.Settling => next is TradePlanItemState.Completed or TradePlanItemState.NeedsAttention or TradePlanItemState.Failed,
            TradePlanItemState.NeedsAttention => next is TradePlanItemState.Pending or TradePlanItemState.Completed or TradePlanItemState.Skipped or TradePlanItemState.Failed,
            TradePlanItemState.Completed or TradePlanItemState.Skipped or TradePlanItemState.Failed => false,
            _ => false,
        };

    public static bool CanTransitionTo(this TradeOperationState current, TradeOperationState next) =>
        current switch
        {
            TradeOperationState.Queued => next is TradeOperationState.Running or TradeOperationState.Paused or TradeOperationState.Failed or TradeOperationState.Cancelled,
            TradeOperationState.Running => next is TradeOperationState.Paused or TradeOperationState.NeedsAttention or TradeOperationState.Completed or TradeOperationState.Failed or TradeOperationState.Cancelled,
            TradeOperationState.Paused => next is TradeOperationState.Queued or TradeOperationState.Running or TradeOperationState.Failed or TradeOperationState.Cancelled,
            TradeOperationState.NeedsAttention => next is TradeOperationState.Running or TradeOperationState.Paused or TradeOperationState.Failed or TradeOperationState.Cancelled,
            TradeOperationState.Completed or TradeOperationState.Failed or TradeOperationState.Cancelled => false,
            _ => false,
        };
}
