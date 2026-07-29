using System;
using System.Collections.Generic;

namespace SysBot.Pokemon;

public sealed record TradePlanItemDraft(
    string ItemId,
    string ClientItemId,
    int Position,
    string ShowdownSet);

public sealed record TradePlanDraft(
    string PlanId,
    string OwnerId,
    ProgramMode GameMode,
    string AccessJson,
    TradePlanPolicies Policies,
    IReadOnlyList<TradePlanItemDraft> Items,
    DateTimeOffset CreatedAt);

public sealed record TradePlanSnapshot(
    string PlanId,
    string OwnerId,
    ProgramMode GameMode,
    TradePlanState State,
    string AccessJson,
    TradePlanPolicies Policies,
    string? ValidationRuntimeGeneration,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version,
    IReadOnlyList<TradePlanItemSnapshot> Items);

public sealed record TradePlanItemSnapshot(
    string ItemId,
    string PlanId,
    string ClientItemId,
    int Position,
    string ShowdownSet,
    TradePlanItemState State,
    string? PreparedHash,
    int AttemptCount,
    string? LastErrorJson,
    string? SettlementEvidenceJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version);

public sealed record TradeOperationSnapshot(
    string OperationId,
    string PlanId,
    TradeOperationState State,
    string? CurrentItemId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version);

public sealed record TradeEventSnapshot(
    long EventId,
    long Sequence,
    string? OperationId,
    string PlanId,
    string? ItemId,
    string EventType,
    string DetailsJson,
    DateTimeOffset OccurredAt);

public sealed record TradeAttemptSnapshot(
    string AttemptId,
    string OperationId,
    string ItemId,
    int AttemptNumber,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string? FailureCode,
    bool IrreversibleBoundaryCrossed);

public sealed record TradeLeaseSnapshot(
    string BotInstanceId,
    string OperationId,
    string OwnerTokenHash,
    DateTimeOffset AcquiredAt,
    DateTimeOffset ExpiresAt,
    long Revision);

public sealed record TradeLeaseAcquireResult(
    bool Acquired,
    TradeLeaseSnapshot Current);

public enum TradeStoreIdempotencyOutcome
{
    Created,
    Replayed,
}

public sealed record TradeStoreIdempotencyResult<T>(
    TradeStoreIdempotencyOutcome Outcome,
    T Resource);

public sealed class TradeStoreConflictException : InvalidOperationException
{
    public TradeStoreConflictException(string message) : base(message)
    {
    }
}

public sealed class TradeStoreConcurrencyException : InvalidOperationException
{
    public TradeStoreConcurrencyException(string message) : base(message)
    {
    }
}

public sealed class TradeStoreNotFoundException : InvalidOperationException
{
    public TradeStoreNotFoundException(string message) : base(message)
    {
    }
}
