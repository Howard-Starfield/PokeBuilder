using System.Collections.Generic;

namespace SysBot.Pokemon;

public sealed record TradeControlResponse<T>(
    bool Success,
    T? Data,
    TradeControlError? Error)
{
    public static TradeControlResponse<T> Ok(T data) => new(true, data, null);

    public static TradeControlResponse<T> Fail(TradeControlError error) =>
        new(false, default, error);
}

public enum TradeAttentionResolution
{
    MarkCompleted,
    RetryCurrent,
    SkipItem,
    FailPlan,
}

/// <summary>
/// Contract consumed by delivery adapters such as MCP and the website.
/// Authentication is performed by the adapter and supplied as ownerId.
/// </summary>
public interface ITradeControlApi
{
    TradeControlResponse<TradeRuntimeSnapshot> ListBotInstances(
        string ownerId,
        bool includeOffline);

    TradeControlResponse<TradePlanStructuralValidation> ValidateTradePlan(
        CreateTradePlanCommand command);

    TradeControlResponse<TradePlanSnapshot> CreateTradePlan(
        CreateTradePlanCommand command);

    TradeControlResponse<TradePlanSnapshot> GetTradePlan(
        string ownerId,
        string planId);

    TradeControlResponse<TradeOperationSnapshot> EnqueueTradePlan(
        string ownerId,
        string planId,
        string idempotencyKey);

    TradeControlResponse<TradeOperationSnapshot> GetTradeOperation(
        string ownerId,
        string operationId);

    TradeControlResponse<IReadOnlyList<TradeEventSnapshot>> ListTradeEvents(
        string ownerId,
        string operationId,
        long afterSequence,
        int limit);

    TradeControlResponse<TradeOperationSnapshot> PauseTradeOperation(
        string ownerId,
        string operationId,
        string idempotencyKey,
        string reason);

    TradeControlResponse<TradeOperationSnapshot> ResumeTradeOperation(
        string ownerId,
        string operationId,
        string idempotencyKey);

    TradeControlResponse<TradeOperationSnapshot> CancelTradeOperation(
        string ownerId,
        string operationId,
        string idempotencyKey,
        bool confirm,
        string reason);

    TradeControlResponse<TradeOperationSnapshot> ResolveTradeAttention(
        string ownerId,
        string operationId,
        string idempotencyKey,
        string itemId,
        TradeAttentionResolution resolution,
        bool confirm,
        string reason);
}
