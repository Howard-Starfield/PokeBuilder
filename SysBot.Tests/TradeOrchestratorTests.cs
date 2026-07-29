using FluentAssertions;
using Microsoft.Data.Sqlite;
using PKHeX.Core;
using SysBot.Pokemon;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace SysBot.Tests;

public sealed class TradeOrchestratorTests
{
    [Fact]
    public void MultiItemFlow_DefersPauseAtIrreversibleBoundary_ThenResumes()
    {
        using var database = new TemporaryDatabase();
        var fixture = database.CreateFixture();
        using var orchestrator = fixture.Orchestrator;
        var plan = CreatePlan(orchestrator, 2);
        var enqueued = orchestrator.EnqueueTradePlan(
            "owner-a",
            plan.PlanId,
            "enqueue-flow-0001");
        enqueued.Success.Should().BeTrue();
        var operationId = enqueued.Data!.OperationId;
        fixture.Queue.WaitForEnqueueCount(1);

        var first = plan.Items[0].ItemId;
        var second = plan.Items[1].ItemId;
        fixture.Queue.Emit(TradeQueueEventKind.BatchProgress, operationId, first, 1);
        fixture.Queue.Emit(TradeQueueEventKind.Confirming, operationId, first, 1);

        var pause = orchestrator.PauseTradeOperation(
            "owner-a",
            operationId,
            "pause-flow-0001",
            "Pause after the current confirmed trade.");
        pause.Success.Should().BeTrue();
        pause.Data!.State.Should().Be(TradeOperationState.Running);

        fixture.Queue.Emit(TradeQueueEventKind.Settling, operationId, first, 1);
        fixture.Queue.Emit(TradeQueueEventKind.BatchProgress, operationId, second, 2);
        WaitUntil(() =>
            fixture.Store.GetOperation(operationId)!.State ==
            TradeOperationState.Paused);

        var pausedPlan = fixture.Store.GetPlan(plan.PlanId)!;
        pausedPlan.Items[0].State.Should().Be(TradePlanItemState.Completed);
        pausedPlan.Items[1].State.Should().Be(TradePlanItemState.Prepared);
        fixture.Queue.Registrations[0].CancellationRequested.Should().BeTrue();

        var resumed = orchestrator.ResumeTradeOperation(
            "owner-a",
            operationId,
            "resume-flow-0001");
        resumed.Success.Should().BeTrue();
        fixture.Queue.WaitForEnqueueCount(2);

        fixture.Queue.Emit(TradeQueueEventKind.BatchProgress, operationId, second, 1);
        fixture.Queue.Emit(TradeQueueEventKind.Confirming, operationId, second, 1);
        fixture.Queue.Emit(TradeQueueEventKind.Settling, operationId, second, 1);
        fixture.Queue.Emit(TradeQueueEventKind.Completed, operationId, second, 1);
        WaitUntil(() =>
            fixture.Store.GetOperation(operationId)!.State ==
            TradeOperationState.Completed);

        var completed = fixture.Store.GetPlan(plan.PlanId)!;
        completed.State.Should().Be(TradePlanState.Completed);
        completed.Items.Should().OnlyContain(z =>
            z.State == TradePlanItemState.Completed);
        fixture.Store.GetLease("switch-a:0").Should().BeNull();
    }

    [Fact]
    public void RestartAfterConfirmation_EntersNeedsAttention_WithoutRequeue()
    {
        using var database = new TemporaryDatabase();
        var first = database.CreateFixture();
        var plan = CreatePlan(first.Orchestrator, 1);
        var operation = first.Orchestrator.EnqueueTradePlan(
            "owner-a",
            plan.PlanId,
            "enqueue-restart-0001").Data!;
        first.Queue.WaitForEnqueueCount(1);
        var itemId = plan.Items[0].ItemId;
        first.Queue.Emit(
            TradeQueueEventKind.BatchProgress,
            operation.OperationId,
            itemId,
            1);
        first.Queue.Emit(
            TradeQueueEventKind.Confirming,
            operation.OperationId,
            itemId,
            1);
        WaitUntil(() =>
            first.Store.GetPlan(plan.PlanId)!.Items[0].State ==
            TradePlanItemState.Confirming);
        first.Orchestrator.Dispose();

        var restarted = database.CreateFixture();
        using var restartedOrchestrator = restarted.Orchestrator;
        restartedOrchestrator.RecoverNonterminalOperations();

        WaitUntil(() =>
            restarted.Store.GetOperation(operation.OperationId)!.State ==
            TradeOperationState.NeedsAttention);
        var recoveredPlan = restarted.Store.GetPlan(plan.PlanId)!;
        recoveredPlan.State.Should().Be(TradePlanState.NeedsAttention);
        recoveredPlan.Items[0].State
            .Should().Be(TradePlanItemState.NeedsAttention);
        restarted.Queue.EnqueueCount.Should().Be(0);
        restarted.Store.GetAttempts(itemId).Single()
            .IrreversibleBoundaryCrossed.Should().BeTrue();
    }

    [Fact]
    public void RestartBeforeConfirmation_ReacquiresDispatcherAndRequeues()
    {
        using var database = new TemporaryDatabase();
        var first = database.CreateFixture();
        var plan = CreatePlan(first.Orchestrator, 1);
        var operation = first.Orchestrator.EnqueueTradePlan(
            "owner-a",
            plan.PlanId,
            "enqueue-restart-safe-0001").Data!;
        first.Queue.WaitForEnqueueCount(1);
        var itemId = plan.Items[0].ItemId;
        first.Queue.Emit(
            TradeQueueEventKind.BatchProgress,
            operation.OperationId,
            itemId,
            1);
        WaitUntil(() =>
            first.Store.GetPlan(plan.PlanId)!.Items[0].State ==
            TradePlanItemState.Searching);
        first.Orchestrator.Dispose();

        var restarted = database.CreateFixture();
        using var restartedOrchestrator = restarted.Orchestrator;
        restartedOrchestrator.RecoverNonterminalOperations();
        restarted.Queue.WaitForEnqueueCount(1);

        restarted.Store.GetOperation(operation.OperationId)!.State
            .Should().Be(TradeOperationState.Running);
        restarted.Store.GetPlan(plan.PlanId)!.Items[0].State
            .Should().Be(TradePlanItemState.Prepared);
        restarted.Store.GetAttempts(itemId).Single().FailureCode
            .Should().Be(TradeControlErrorCodes.TransportDisconnected);
        restarted.Store.GetLease("switch-a:0")!.OperationId
            .Should().Be(operation.OperationId);
    }

    [Fact]
    public void PartnerDisconnectBeforeConfirmation_RequeuesCurrentItem()
    {
        using var database = new TemporaryDatabase();
        var fixture = database.CreateFixture();
        using var orchestrator = fixture.Orchestrator;
        var plan = CreatePlan(orchestrator, 1);
        var operation = orchestrator.EnqueueTradePlan(
            "owner-a",
            plan.PlanId,
            "enqueue-retry-0001").Data!;
        fixture.Queue.WaitForEnqueueCount(1);
        var itemId = plan.Items[0].ItemId;
        fixture.Queue.Emit(
            TradeQueueEventKind.BatchProgress,
            operation.OperationId,
            itemId,
            1);
        fixture.Queue.Emit(
            TradeQueueEventKind.Cancelled,
            operation.OperationId,
            itemId,
            1,
            PokeTradeResult.TrainerLeft.ToString());

        fixture.Queue.WaitForEnqueueCount(2);
        var retried = fixture.Store.GetPlan(plan.PlanId)!.Items[0];
        retried.State.Should().Be(TradePlanItemState.Prepared);
        retried.AttemptCount.Should().Be(1);
        fixture.Store.GetAttempts(itemId).Single().FailureCode
            .Should().Be(TradeControlErrorCodes.PartnerDisconnected);
    }

    [Fact]
    public void OwnershipAndExplicitConfirmation_AreEnforced()
    {
        using var database = new TemporaryDatabase();
        var fixture = database.CreateFixture();
        using var orchestrator = fixture.Orchestrator;
        var plan = CreatePlan(orchestrator, 1);
        var operation = orchestrator.EnqueueTradePlan(
            "owner-a",
            plan.PlanId,
            "enqueue-owner-0001").Data!;
        fixture.Queue.WaitForEnqueueCount(1);

        orchestrator.GetTradePlan("owner-b", plan.PlanId).Success
            .Should().BeFalse();
        orchestrator.GetTradeOperation("owner-b", operation.OperationId).Success
            .Should().BeFalse();
        var cancel = orchestrator.CancelTradeOperation(
            "owner-a",
            operation.OperationId,
            "cancel-owner-0001",
            confirm: false,
            "Testing confirmation.");
        cancel.Success.Should().BeFalse();
        cancel.Error!.Code.Should().Be(
            TradeControlErrorCodes.ConfirmationRequired);
    }

    [Fact]
    public void OperationMutationKeys_ReplaySafely_AndRejectDifferentCommands()
    {
        using var database = new TemporaryDatabase();
        var fixture = database.CreateFixture();
        using var orchestrator = fixture.Orchestrator;
        var plan = CreatePlan(orchestrator, 1);
        var operation = orchestrator.EnqueueTradePlan(
            "owner-a",
            plan.PlanId,
            "enqueue-command-idem-0001").Data!;
        fixture.Queue.WaitForEnqueueCount(1);

        var first = orchestrator.PauseTradeOperation(
            "owner-a",
            operation.OperationId,
            "shared-command-key-0001",
            "Pause for operator review.");
        first.Success.Should().BeTrue();
        first.Data!.State.Should().Be(TradeOperationState.Paused);

        var replay = orchestrator.PauseTradeOperation(
            "owner-a",
            operation.OperationId,
            "shared-command-key-0001",
            "Pause for operator review.");
        replay.Success.Should().BeTrue();
        replay.Data!.State.Should().Be(TradeOperationState.Paused);

        var conflict = orchestrator.CancelTradeOperation(
            "owner-a",
            operation.OperationId,
            "shared-command-key-0001",
            confirm: true,
            "A different command using the same key.");
        conflict.Success.Should().BeFalse();
        conflict.Error!.Code.Should().Be(TradeControlErrorCodes.PlanConflict);
    }

    [Fact]
    public void ReconnectPolicy_IsImmediateThenStagedAndBounded()
    {
        TradeReconnectPolicy.GetDelayBeforeAttempt(0, 900).Should().Be(0);
        TradeReconnectPolicy.GetDelayBeforeAttempt(1, 0).Should().Be(250);
        TradeReconnectPolicy.GetDelayBeforeAttempt(2, 0).Should().Be(1_000);
        TradeReconnectPolicy.GetDelayBeforeAttempt(3, 0).Should().Be(5_000);
        TradeReconnectPolicy.GetDelayBeforeAttempt(4, 0).Should().Be(30_000);
        TradeReconnectPolicy.GetDelayBeforeAttempt(20, 500).Should().Be(30_500);
    }

    [Fact]
    public void WebsiteQueueHints_UseSharedDispatcher_AndAccountOnlySettledItems()
    {
        using var database = new TemporaryDatabase();
        var fixture = database.CreateFixture();
        using var orchestrator = fixture.Orchestrator;
        var trainerId = BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0);
        var reservation = TradeRateLimitService.Instance.TryReserve(
            trainerId,
            requestedCount: 2,
            limit: 2,
            windowMinutes: 60);
        reservation.Allowed.Should().BeTrue();
        var ownerId = $"website:{trainerId}:favored";
        var created = orchestrator.CreateTradePlan(new(
            ownerId,
            ProgramMode.SV,
            """{"link_code":"13333333"}""",
            new TradePlanPolicies
            {
                TransportReconnectDelaysMs = [0, 1, 1, 1, 1],
            },
            [
                new("website-001", "Pikachu\nLevel: 50"),
                new("website-002", "Eevee\nLevel: 50"),
            ],
            "website-create-0001"));
        created.Success.Should().BeTrue();

        var enqueued = orchestrator.EnqueueTradePlanWithQueueHints(
            ownerId,
            created.Data!.PlanId,
            "website-enqueue-0001",
            new(
                trainerId,
                "Website User",
                IsFavored: true,
                reservation.ReservationId));
        enqueued.Success.Should().BeTrue();
        fixture.Queue.WaitForEnqueueCount(1);

        var request = fixture.Queue.Requests.Single();
        request.TrainerId.Should().Be(trainerId);
        request.TrainerName.Should().Be("Website User");
        request.IsFavored.Should().BeTrue();
        request.RateLimitReservationId.Should().Be(reservation.ReservationId);
        var admission = orchestrator.GetQueueAdmission(
            ownerId,
            enqueued.Data!.OperationId);
        admission.Success.Should().BeTrue();
        admission.Data!.QueuePosition.Should().Be(1);

        var first = created.Data.Items[0].ItemId;
        var second = created.Data.Items[1].ItemId;
        fixture.Queue.Emit(
            TradeQueueEventKind.BatchProgress,
            enqueued.Data.OperationId,
            first,
            1);
        fixture.Queue.Emit(
            TradeQueueEventKind.Confirming,
            enqueued.Data.OperationId,
            first,
            1);
        fixture.Queue.Emit(
            TradeQueueEventKind.Settling,
            enqueued.Data.OperationId,
            first,
            1);
        fixture.Queue.Emit(
            TradeQueueEventKind.BatchProgress,
            enqueued.Data.OperationId,
            second,
            2);

        orchestrator.CancelTradeOperation(
            ownerId,
            enqueued.Data.OperationId,
            "website-cancel-0001",
            confirm: true,
            "Cancel the remaining item.").Success.Should().BeTrue();

        var next = TradeRateLimitService.Instance.TryReserve(
            trainerId,
            requestedCount: 1,
            limit: 2,
            windowMinutes: 60);
        next.Allowed.Should().BeTrue(
            "one settled item is consumed and the canceled remainder is released");
        TradeRateLimitService.Instance.ReleaseReservation(next.ReservationId!);
    }

    private static TradePlanSnapshot CreatePlan(
        TradeOrchestrator orchestrator,
        int itemCount)
    {
        var items = Enumerable.Range(1, itemCount)
            .Select(index => new TradePlanRequestItem(
                $"client-{index}",
                $"Pikachu\nLevel: {49 + index}"))
            .ToArray();
        var created = orchestrator.CreateTradePlan(new(
            "owner-a",
            ProgramMode.SV,
            """{"link_code":"13333333"}""",
            new TradePlanPolicies
            {
                TransportReconnectDelaysMs = [0, 1, 1, 1, 1],
            },
            items,
            $"create-plan-{itemCount:0000}"));
        created.Success.Should().BeTrue();
        return created.Data!;
    }

    private static void WaitUntil(
        Func<bool> predicate,
        int timeoutMilliseconds = 5_000)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
        while (!predicate())
        {
            if (Environment.TickCount64 >= deadline)
                throw new TimeoutException("Timed out waiting for orchestrator state.");
            Thread.Sleep(10);
        }
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"pokebot-orchestrator-tests-{Guid.NewGuid():N}");

        public TemporaryDatabase() => Directory.CreateDirectory(_directory);

        public string Path => System.IO.Path.Combine(
            _directory,
            "trade-control.sqlite3");

        public Fixture CreateFixture()
        {
            var store = new SqliteTradePlanStore(Path);
            store.Initialize();
            var clock = new FixedClock();
            var runtime = new FakeRuntime();
            var queue = new FakeQueueAdapter();
            var planService = new TradePlanApplicationService(
                store,
                clock,
                new Uuid7TradeControlIdGenerator());
            var orchestrator = new TradeOrchestrator(
                store,
                planService,
                runtime,
                queue,
                clock,
                new Uuid7TradeOperationIdGenerator());
            return new(store, queue, orchestrator);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed record Fixture(
        SqliteTradePlanStore Store,
        FakeQueueAdapter Queue,
        TradeOrchestrator Orchestrator);

    private sealed class FixedClock : ITradeControlClock
    {
        private long _ticks = DateTimeOffset.Parse(
            "2026-07-29T12:00:00Z").UtcTicks;

        public DateTimeOffset UtcNow =>
            new(Interlocked.Add(ref _ticks, TimeSpan.TicksPerMillisecond), TimeSpan.Zero);
    }

    private sealed class FakeRuntime : ICurrentTradeRuntime
    {
        private readonly TradeRuntimeSnapshot _snapshot = new(
            ProgramMode.SV,
            true,
            true,
            true,
            0,
            "runtime-test",
            [
                new(
                    "switch-a:0",
                    "switch-a",
                    true,
                    false,
                    false,
                    true,
                    PokeRoutineType.LinkTrade),
            ]);

        public TradeRuntimeSnapshot Inspect() => _snapshot;

        public TradeRuntimeResolution Resolve(
            ProgramMode expectedMode,
            string? expectedGeneration = null,
            bool requireRunning = true,
            bool requireOpenQueue = true) =>
            throw new NotSupportedException();
    }

    private sealed class FakeQueueAdapter : ITradeQueueAdapter
    {
        private readonly object _sync = new();
        private ITradeQueueObserver? _observer;

        public int EnqueueCount
        {
            get
            {
                lock (_sync)
                    return Registrations.Count;
            }
        }

        public List<FakeRegistration> Registrations { get; } = [];

        public List<TradeQueueEnqueueRequest> Requests { get; } = [];

        public TradePreparationResult Prepare(
            ProgramMode mode,
            string runtimeGeneration,
            string itemId,
            string showdownSet) =>
            new(
                new(
                    itemId,
                    mode,
                    new PK9(),
                    $"hash-{itemId}"),
                null);

        public TradeQueueEnqueueResult Enqueue(
            TradeQueueEnqueueRequest request,
            ITradeQueueObserver observer)
        {
            lock (_sync)
            {
                _observer = observer;
                Requests.Add(request);
                var registration = new FakeRegistration(
                    request.OperationId,
                    Registrations.Count + 1);
                Registrations.Add(registration);
                Monitor.PulseAll(_sync);
                return new(registration, null);
            }
        }

        public void Emit(
            TradeQueueEventKind kind,
            string operationId,
            string itemId,
            int batchPosition,
            string? resultCode = null)
        {
            ITradeQueueObserver observer;
            lock (_sync)
                observer = _observer!;
            observer.OnEvent(new(
                kind,
                operationId,
                itemId,
                batchPosition,
                null,
                resultCode));
        }

        public void WaitForEnqueueCount(int count)
        {
            lock (_sync)
            {
                var deadline = Environment.TickCount64 + 5_000;
                while (Registrations.Count < count)
                {
                    var remaining = deadline - Environment.TickCount64;
                    if (remaining <= 0)
                        throw new TimeoutException("Timed out waiting for queue admission.");
                    Monitor.Wait(_sync, (int)Math.Min(remaining, 100));
                }
            }
        }
    }

    private sealed class FakeRegistration : ITradeQueueRegistration
    {
        public FakeRegistration(string operationId, int queuePosition)
        {
            OperationId = operationId;
            QueuePosition = queuePosition;
        }

        public string OperationId { get; }

        public int QueuePosition { get; }

        public int BypassedCount => 0;

        public bool CancellationRequested { get; private set; }

        public void RequestCancellation() => CancellationRequested = true;
    }
}
