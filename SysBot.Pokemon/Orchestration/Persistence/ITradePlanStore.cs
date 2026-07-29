using System;
using System.Collections.Generic;

namespace SysBot.Pokemon;

public interface ITradePlanStore
{
    void Initialize();

    int GetSchemaVersion();

    TradeStoreIdempotencyResult<TradePlanSnapshot> CreatePlan(
        TradePlanDraft draft,
        string idempotencyScope,
        string idempotencyKey,
        string requestHash);

    TradePlanSnapshot? GetPlan(string planId);

    TradePlanSnapshot TransitionPlan(
        string planId,
        TradePlanState expectedState,
        TradePlanState nextState,
        string eventType,
        string detailsJson,
        DateTimeOffset occurredAt,
        string? validationRuntimeGeneration = null,
        string? operationId = null);

    TradePlanItemSnapshot PrepareItem(
        string planId,
        string itemId,
        string preparedHash,
        DateTimeOffset occurredAt);

    TradeStoreIdempotencyResult<TradeOperationSnapshot> CreateOperation(
        string operationId,
        string planId,
        string idempotencyScope,
        string idempotencyKey,
        string requestHash,
        DateTimeOffset createdAt);

    TradeOperationSnapshot? GetOperation(string operationId);

    TradeStoreIdempotencyOutcome ClaimOperationCommand(
        string operationId,
        string idempotencyScope,
        string idempotencyKey,
        string requestHash,
        DateTimeOffset createdAt);

    IReadOnlyList<TradeOperationSnapshot> ListRecoverableOperations();

    TradeOperationSnapshot TransitionOperation(
        string operationId,
        TradeOperationState expectedOperationState,
        TradeOperationState nextOperationState,
        TradePlanState expectedPlanState,
        TradePlanState nextPlanState,
        string eventType,
        string detailsJson,
        DateTimeOffset occurredAt);

    TradePlanItemSnapshot TransitionItem(
        string operationId,
        TradeOperationState expectedOperationState,
        string itemId,
        TradePlanItemState expectedState,
        TradePlanItemState nextState,
        string eventType,
        string detailsJson,
        DateTimeOffset occurredAt,
        string? lastErrorJson = null,
        string? settlementEvidenceJson = null);

    TradeAttemptSnapshot StartAttempt(
        string attemptId,
        string operationId,
        string itemId,
        int attemptNumber,
        DateTimeOffset startedAt);

    TradeAttemptSnapshot FinishAttempt(
        string attemptId,
        DateTimeOffset endedAt,
        string? failureCode,
        bool irreversibleBoundaryCrossed);

    IReadOnlyList<TradeAttemptSnapshot> GetAttempts(string itemId);

    IReadOnlyList<TradeEventSnapshot> ListPlanEvents(
        string planId,
        long afterSequence = 0,
        int limit = 200);

    IReadOnlyList<TradeEventSnapshot> ListEvents(
        string operationId,
        long afterSequence = 0,
        int limit = 200);

    TradeLeaseAcquireResult TryAcquireLease(
        string botInstanceId,
        string operationId,
        string ownerTokenHash,
        DateTimeOffset acquiredAt,
        DateTimeOffset expiresAt);

    bool RenewLease(
        string botInstanceId,
        string operationId,
        string ownerTokenHash,
        DateTimeOffset now,
        DateTimeOffset newExpiresAt);

    bool ReleaseLease(
        string botInstanceId,
        string operationId,
        string ownerTokenHash);

    TradeLeaseSnapshot? GetLease(string botInstanceId);
}
