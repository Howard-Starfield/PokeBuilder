using PKHeX.Core;
using PKHeX.Core.AutoMod;
using SysBot.Pokemon.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SysBot.Pokemon;

public enum TradeQueueEventKind
{
    Initialized,
    Searching,
    Message,
    BatchProgress,
    Confirming,
    Settling,
    Completed,
    Cancelled,
}

public sealed record TradeQueueEvent(
    TradeQueueEventKind Kind,
    string OperationId,
    string? ItemId,
    int? BatchPosition,
    string? Message,
    string? ResultCode);

public interface ITradeQueueObserver
{
    void OnEvent(TradeQueueEvent tradeEvent);
}

public sealed record PreparedTradeItem(
    string ItemId,
    ProgramMode GameMode,
    PKM Pokemon,
    string PreparedHash);

public sealed record TradePreparationResult(
    PreparedTradeItem? Prepared,
    TradeControlError? Error)
{
    public bool IsSuccess => Prepared is not null && Error is null;
}

public sealed record TradeQueueEnqueueRequest(
    string OperationId,
    string OwnerId,
    string TrainerName,
    ProgramMode GameMode,
    string RuntimeGeneration,
    string AccessJson,
    TradeEvolutionPolicy EvolutionPolicy,
    IReadOnlyList<PreparedTradeItem> Items,
    ulong? TrainerId = null,
    bool IsFavored = false,
    string? RateLimitReservationId = null);

public interface ITradeQueueRegistration
{
    string OperationId { get; }

    int QueuePosition { get; }

    int BypassedCount { get; }

    int QueueCount { get; }

    float EstimatedWaitMinutes { get; }

    void RequestCancellation();
}

public sealed record TradeQueueEnqueueResult(
    ITradeQueueRegistration? Registration,
    TradeControlError? Error)
{
    public bool IsSuccess => Registration is not null && Error is null;
}

public interface ITradeQueueAdapter
{
    TradePreparationResult Prepare(
        ProgramMode mode,
        string runtimeGeneration,
        string itemId,
        string showdownSet);

    TradeQueueEnqueueResult Enqueue(
        TradeQueueEnqueueRequest request,
        ITradeQueueObserver observer);
}

/// <summary>
/// Typed adapter over the existing SysBot queues. It re-resolves the current
/// runner and rechecks legality and blocked-item policy at the enqueue boundary.
/// </summary>
public sealed class SysBotTradeQueueAdapter : ITradeQueueAdapter
{
    private readonly ICurrentTradeRuntime _runtime;

    public SysBotTradeQueueAdapter(ICurrentTradeRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    public TradePreparationResult Prepare(
        ProgramMode mode,
        string runtimeGeneration,
        string itemId,
        string showdownSet)
    {
        var resolution = _runtime.Resolve(
            mode,
            runtimeGeneration,
            requireRunning: false,
            requireOpenQueue: false);
        if (!resolution.IsSuccess)
            return new(null, resolution.Error);

        if (string.IsNullOrWhiteSpace(showdownSet) || showdownSet.Length > 8192)
        {
            return Failure(
                TradeControlErrorCodes.InvalidRequest,
                "Showdown set is required and cannot exceed 8192 characters.",
                ("item_id", itemId));
        }

        try
        {
            AutoLegalityWrapper.EnsureInitialized(resolution.Runner!.Config.Legality);
            var trainer = GetTrainerInfo(mode);
            if (trainer is null)
            {
                return Failure(
                    TradeControlErrorCodes.ModeMismatch,
                    $"No legality trainer is available for {mode}.");
            }

            var template = AutoLegalityWrapper.GetTemplate(new ShowdownSet(showdownSet));
            var generated = TradeExtensions<PK9>.IsEggCheck(showdownSet)
                ? trainer.GenerateEgg(template, out _)
                : trainer.GetLegal(template, out _);

            if (generated is null || !IsExpectedFormat(mode, generated) ||
                !new LegalityAnalysis(generated).Valid)
            {
                return Failure(
                    TradeControlErrorCodes.LegalityFailed,
                    "The Showdown set could not be prepared as a legal Pokémon for the current game.",
                    ("item_id", itemId),
                    ("game_mode", mode.ToString()));
            }

            if (IsBlocked(mode, generated))
            {
                return Failure(
                    TradeControlErrorCodes.ItemBlocked,
                    "The prepared Pokémon holds an item blocked by the game or SysBot policy.",
                    ("item_id", itemId),
                    ("held_item", generated.HeldItem));
            }

            var hashInput = Encoding.UTF8.GetBytes(
                $"{mode}:{Convert.ToHexString(generated.Data)}");
            var hash = Convert.ToHexString(SHA256.HashData(hashInput))
                .ToLowerInvariant();
            return new(
                new(itemId, mode, generated, hash),
                null);
        }
        catch (Exception ex)
        {
            return Failure(
                TradeControlErrorCodes.LegalityFailed,
                "The Showdown set could not be prepared for trade.",
                ("item_id", itemId),
                ("reason", ex.GetType().Name));
        }
    }

    public TradeQueueEnqueueResult Enqueue(
        TradeQueueEnqueueRequest request,
        ITradeQueueObserver observer)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(observer);

        var resolution = _runtime.Resolve(
            request.GameMode,
            request.RuntimeGeneration,
            requireRunning: true,
            requireOpenQueue: true);
        if (!resolution.IsSuccess)
            return new(null, resolution.Error);

        if (request.Items.Count == 0)
        {
            return EnqueueFailure(
                TradeControlErrorCodes.InvalidRequest,
                "At least one prepared item is required.");
        }

        if (request.Items.Any(item =>
            item.GameMode != request.GameMode ||
            !IsExpectedFormat(request.GameMode, item.Pokemon)))
        {
            return EnqueueFailure(
                TradeControlErrorCodes.ModeMismatch,
                "One or more prepared items do not match the current game mode.");
        }

        if (request.Items.Any(item => IsBlocked(request.GameMode, item.Pokemon)))
        {
            return EnqueueFailure(
                TradeControlErrorCodes.ItemBlocked,
                "One or more prepared Pokémon now hold a blocked item.");
        }

        if (!TryParseAccess(
            request.GameMode,
            request.AccessJson,
            out var code,
            out var pictocodes,
            out var accessError))
        {
            return new(null, accessError);
        }

        try
        {
            return request.GameMode switch
            {
                ProgramMode.SWSH => EnqueueTyped(
                    (PokeBotRunner<PK8>)resolution.Runner!,
                    request,
                    request.Items.Select(z => (PK8)z.Pokemon).ToArray(),
                    code,
                    pictocodes,
                    observer),
                ProgramMode.BDSP => EnqueueTyped(
                    (PokeBotRunner<PB8>)resolution.Runner!,
                    request,
                    request.Items.Select(z => (PB8)z.Pokemon).ToArray(),
                    code,
                    pictocodes,
                    observer),
                ProgramMode.LA => EnqueueTyped(
                    (PokeBotRunner<PA8>)resolution.Runner!,
                    request,
                    request.Items.Select(z => (PA8)z.Pokemon).ToArray(),
                    code,
                    pictocodes,
                    observer),
                ProgramMode.SV => EnqueueTyped(
                    (PokeBotRunner<PK9>)resolution.Runner!,
                    request,
                    request.Items.Select(z => (PK9)z.Pokemon).ToArray(),
                    code,
                    pictocodes,
                    observer),
                ProgramMode.LGPE => EnqueueTyped(
                    (PokeBotRunner<PB7>)resolution.Runner!,
                    request,
                    request.Items.Select(z => (PB7)z.Pokemon).ToArray(),
                    code,
                    pictocodes,
                    observer),
                ProgramMode.LZA => EnqueueTyped(
                    (PokeBotRunner<PA9>)resolution.Runner!,
                    request,
                    request.Items.Select(z => (PA9)z.Pokemon).ToArray(),
                    code,
                    pictocodes,
                    observer),
                _ => EnqueueFailure(
                    TradeControlErrorCodes.ModeMismatch,
                    "The current game mode is not supported."),
            };
        }
        catch (InvalidCastException)
        {
            return EnqueueFailure(
                TradeControlErrorCodes.ModeMismatch,
                "The current runner type does not match its configured game mode.");
        }
        catch (Exception ex)
        {
            return EnqueueFailure(
                TradeControlErrorCodes.BotBusy,
                $"The queue rejected the operation ({ex.GetType().Name}).");
        }
    }

    private static TradeQueueEnqueueResult EnqueueTyped<T>(
        PokeBotRunner<T> runner,
        TradeQueueEnqueueRequest request,
        IReadOnlyList<T> pokemon,
        int code,
        IReadOnlyList<Pictocodes> pictocodes,
        ITradeQueueObserver observer)
        where T : PKM, new()
    {
        var hub = runner.Hub;
        if (!hub.Queues.Info.GetCanQueue())
        {
            return EnqueueFailure(
                TradeControlErrorCodes.QueueClosed,
                "The current trade queue is closed or has no ready trade bot.");
        }

        var trainerId = request.TrainerId ?? StableTrainerId(request.OwnerId);
        var trainer = new PokeTradeTrainerInfo(
            string.IsNullOrWhiteSpace(request.TrainerName)
                ? "MCP trade"
                : request.TrainerName,
            trainerId);
        var notifier = new ControlPlaneTradeNotifier<T>(
            request.OperationId,
            request.Items.Select(z => z.ItemId).ToArray(),
            observer);
        var uniqueId = StableUniqueId(request.OperationId);
        var isBatch = pokemon.Count > 1;
        var preAddEntryCount = hub.Queues.Info.GetTotalEntryCount();
        var detail = new PokeTradeDetail<T>(
            pokemon[0],
            trainer,
            notifier,
            isBatch ? PokeTradeType.Batch : PokeTradeType.Specific,
            code,
            favored: request.IsFavored,
            lgcode: pictocodes.ToList(),
            batchTradeNumber: isBatch ? 1 : 0,
            totalBatchTrades: pokemon.Count,
            uniqueTradeID: uniqueId)
        {
            BatchTrades = isBatch ? pokemon.ToList() : null,
        };
        detail.Context[TradeControlContextKeys.EvolutionPolicy] =
            request.EvolutionPolicy.ToString();
        var routine = isBatch ? PokeRoutineType.Batch : PokeRoutineType.LinkTrade;
        var entry = new TradeEntry<T>(
            detail,
            trainerId,
            routine,
            trainer.TrainerName,
            uniqueId);

        var registration = new TradeQueueRegistration<T>(
            request.OperationId,
            hub,
            entry,
            detail);
        detail.LifecycleObserver = stage => notifier.ReportLifecycle(stage);
        notifier.OnFinish = _ => hub.Queues.Info.Remove(entry);

        var add = hub.Queues.Info.AddToTradeQueue(entry, trainerId);
        if (add != QueueResultAdd.Added)
        {
            var error = add switch
            {
                QueueResultAdd.NotAllowedItem => new TradeControlError(
                    TradeControlErrorCodes.ItemBlocked,
                    "A prepared Pokémon holds an item blocked by policy."),
                QueueResultAdd.QueueFull => new TradeControlError(
                    TradeControlErrorCodes.QueueClosed,
                    "The current trade queue is full."),
                _ => new TradeControlError(
                    TradeControlErrorCodes.BotBusy,
                    "This authenticated owner already has a trade in the queue."),
            };
            return new(null, error);
        }

        var position = hub.Queues.Info
            .CheckPosition(trainerId, uniqueId, routine)
            .Position;
        var bypassed = request.IsFavored
            ? Math.Max(
                0,
                (preAddEntryCount + 1) -
                hub.Queues.Info.GetEntryPosition(trainerId, uniqueId))
            : 0;
        var queueCount = hub.Queues.Info.Count;
        var botCount = Math.Max(1, hub.Bots.Count);
        var estimatedWaitMinutes = position > botCount
            ? hub.Config.Queues.EstimateDelay(position, botCount)
            : 0;
        registration.SetAdmission(
            position,
            bypassed,
            queueCount,
            estimatedWaitMinutes);
        return new(registration, null);
    }

    private static bool TryParseAccess(
        ProgramMode mode,
        string accessJson,
        out int linkCode,
        out IReadOnlyList<Pictocodes> pictocodes,
        out TradeControlError? error)
    {
        linkCode = 0;
        pictocodes = [];
        error = null;
        try
        {
            using var document = JsonDocument.Parse(accessJson);
            if (mode == ProgramMode.LGPE)
            {
                if (!document.RootElement.TryGetProperty("pictocodes", out var values) ||
                    values.ValueKind != JsonValueKind.Array ||
                    values.GetArrayLength() != 3)
                {
                    error = new(
                        TradeControlErrorCodes.InvalidRequest,
                        "LGPE requires exactly three pictocodes.");
                    return false;
                }

                var parsed = new List<Pictocodes>(3);
                foreach (var pictocodeValue in values.EnumerateArray())
                {
                    if (pictocodeValue.ValueKind != JsonValueKind.String ||
                        !Enum.TryParse<Pictocodes>(
                            pictocodeValue.GetString(),
                            ignoreCase: true,
                            out var pictocode) ||
                        !Enum.IsDefined(pictocode))
                    {
                        error = new(
                            TradeControlErrorCodes.InvalidRequest,
                            "LGPE pictocodes must be recognized pictocode names.");
                        return false;
                    }
                    parsed.Add(pictocode);
                }
                pictocodes = parsed;
                return true;
            }

            if (!document.RootElement.TryGetProperty("link_code", out var value) ||
                value.ValueKind != JsonValueKind.String ||
                !int.TryParse(value.GetString(), out linkCode))
            {
                error = new(
                    TradeControlErrorCodes.InvalidRequest,
                    "This game requires an eight-digit numeric link code.");
                return false;
            }
            return true;
        }
        catch (JsonException)
        {
            error = new(
                TradeControlErrorCodes.InvalidRequest,
                "Access must be a valid JSON object.");
            return false;
        }
    }

    private static ITrainerInfo? GetTrainerInfo(ProgramMode mode) => mode switch
    {
        ProgramMode.SWSH => AutoLegalityWrapper.GetTrainerInfo<PK8>(),
        ProgramMode.BDSP => AutoLegalityWrapper.GetTrainerInfo<PB8>(),
        ProgramMode.LA => AutoLegalityWrapper.GetTrainerInfo<PA8>(),
        ProgramMode.SV => AutoLegalityWrapper.GetTrainerInfo<PK9>(),
        ProgramMode.LGPE => AutoLegalityWrapper.GetTrainerInfo<PB7>(),
        ProgramMode.LZA => AutoLegalityWrapper.GetTrainerInfo<PA9>(),
        _ => null,
    };

    private static bool IsExpectedFormat(ProgramMode mode, PKM pokemon) =>
        mode switch
        {
            ProgramMode.SWSH => pokemon is PK8,
            ProgramMode.BDSP => pokemon is PB8,
            ProgramMode.LA => pokemon is PA8,
            ProgramMode.SV => pokemon is PK9,
            ProgramMode.LGPE => pokemon is PB7,
            ProgramMode.LZA => pokemon is PA9,
            _ => false,
        };

    private static bool IsBlocked(ProgramMode mode, PKM pokemon) =>
        mode switch
        {
            ProgramMode.SWSH => TradeExtensions<PK8>.IsItemBlocked(pokemon),
            ProgramMode.BDSP => TradeExtensions<PB8>.IsItemBlocked(pokemon),
            ProgramMode.LA => TradeExtensions<PA8>.IsItemBlocked(pokemon),
            ProgramMode.SV => TradeExtensions<PK9>.IsItemBlocked(pokemon),
            ProgramMode.LGPE => TradeExtensions<PB7>.IsItemBlocked(pokemon),
            ProgramMode.LZA => TradeExtensions<PA9>.IsItemBlocked(pokemon),
            _ => true,
        };

    private static ulong StableTrainerId(string ownerId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ownerId));
        var value = BitConverter.ToUInt64(bytes, 0);
        return value == 0 ? 1UL : value;
    }

    private static int StableUniqueId(string operationId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(operationId));
        return BitConverter.ToInt32(bytes, 0) & int.MaxValue;
    }

    private static TradePreparationResult Failure(
        string code,
        string message,
        params (string Key, object? Value)[] details) =>
        new(null, new(
            code,
            message,
            details.ToDictionary(z => z.Key, z => z.Value)));

    private static TradeQueueEnqueueResult EnqueueFailure(
        string code,
        string message) =>
        new(null, new(code, message));

    private sealed class TradeQueueRegistration<T> : ITradeQueueRegistration
        where T : PKM, new()
    {
        private readonly PokeTradeHub<T> _hub;
        private readonly TradeEntry<T> _entry;
        private readonly PokeTradeDetail<T> _detail;

        public TradeQueueRegistration(
            string operationId,
            PokeTradeHub<T> hub,
            TradeEntry<T> entry,
            PokeTradeDetail<T> detail)
        {
            OperationId = operationId;
            _hub = hub;
            _entry = entry;
            _detail = detail;
        }

        public string OperationId { get; }

        public int QueuePosition { get; private set; }

        public int BypassedCount { get; private set; }

        public int QueueCount { get; private set; }

        public float EstimatedWaitMinutes { get; private set; }

        public void SetAdmission(
            int queuePosition,
            int bypassedCount,
            int queueCount,
            float estimatedWaitMinutes)
        {
            QueuePosition = queuePosition;
            BypassedCount = bypassedCount;
            QueueCount = queueCount;
            EstimatedWaitMinutes = estimatedWaitMinutes;
        }

        public void RequestCancellation()
        {
            _detail.IsCanceled = true;
            _hub.Queues.Info.Remove(_entry);
        }
    }

    private sealed class ControlPlaneTradeNotifier<T> : IPokeTradeNotifier<T>
        where T : PKM, new()
    {
        private readonly string _operationId;
        private readonly IReadOnlyList<string> _itemIds;
        private readonly ITradeQueueObserver _observer;
        private int _currentBatchNumber = 1;

        public ControlPlaneTradeNotifier(
            string operationId,
            IReadOnlyList<string> itemIds,
            ITradeQueueObserver observer)
        {
            _operationId = operationId;
            _itemIds = itemIds;
            _observer = observer;
        }

        public Action<PokeRoutineExecutor<T>>? OnFinish { private get; set; }

        public void TradeInitialize(
            PokeRoutineExecutor<T> routine,
            PokeTradeDetail<T> info) =>
            Emit(TradeQueueEventKind.Initialized, FirstItem(), null, null, null);

        public void TradeSearching(
            PokeRoutineExecutor<T> routine,
            PokeTradeDetail<T> info) =>
            Emit(TradeQueueEventKind.Searching, FirstItem(), null, null, null);

        public void TradeCanceled(
            PokeRoutineExecutor<T> routine,
            PokeTradeDetail<T> info,
            PokeTradeResult msg)
        {
            Emit(
                TradeQueueEventKind.Cancelled,
                CurrentItem(info.BatchTradeNumber),
                info.BatchTradeNumber,
                msg.ToString(),
                msg.ToString());
            OnFinish?.Invoke(routine);
        }

        public void TradeFinished(
            PokeRoutineExecutor<T> routine,
            PokeTradeDetail<T> info,
            T result)
        {
            Emit(
                TradeQueueEventKind.Completed,
                CurrentItem(info.BatchTradeNumber),
                info.BatchTradeNumber,
                "Trade completed.",
                null);
            OnFinish?.Invoke(routine);
        }

        public void SendNotification(
            PokeRoutineExecutor<T> routine,
            PokeTradeDetail<T> info,
            string message) =>
            Emit(
                TradeQueueEventKind.Message,
                CurrentItem(info.BatchTradeNumber),
                info.BatchTradeNumber,
                message,
                null);

        public void SendNotification(
            PokeRoutineExecutor<T> routine,
            PokeTradeDetail<T> info,
            PokeTradeSummary message) =>
            SendNotification(routine, info, message.Summary);

        public void SendNotification(
            PokeRoutineExecutor<T> routine,
            PokeTradeDetail<T> info,
            T result,
            string message) =>
            SendNotification(routine, info, message);

        public void UpdateBatchProgress(
            int currentBatchNumber,
            T currentPokemon,
            int uniqueTradeID)
        {
            _currentBatchNumber = Math.Max(1, currentBatchNumber);
            Emit(
                TradeQueueEventKind.BatchProgress,
                CurrentItem(currentBatchNumber),
                currentBatchNumber,
                null,
                null);
        }

        public void UpdateUniqueTradeID(int uniqueTradeID)
        {
        }

        public void ReportLifecycle(PokeTradeLifecycleStage stage) =>
            Emit(
                stage is PokeTradeLifecycleStage.Confirming
                    ? TradeQueueEventKind.Confirming
                    : TradeQueueEventKind.Settling,
                CurrentItem(_currentBatchNumber),
                _currentBatchNumber,
                null,
                null);

        private string? FirstItem() => _itemIds.Count == 0 ? null : _itemIds[0];

        private string? CurrentItem(int batchNumber)
        {
            var index = Math.Max(0, batchNumber - 1);
            return index < _itemIds.Count ? _itemIds[index] : FirstItem();
        }

        private void Emit(
            TradeQueueEventKind kind,
            string? itemId,
            int? batchPosition,
            string? message,
            string? resultCode) =>
            _observer.OnEvent(new(
                kind,
                _operationId,
                itemId,
                batchPosition,
                message,
                resultCode));
    }
}
