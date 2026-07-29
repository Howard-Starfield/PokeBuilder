using SysBot.Base;
using System;
using System.Collections.Generic;

namespace SysBot.Pokemon;

public sealed record TradeBotInstanceSnapshot(
    string InstanceId,
    string ConnectionName,
    bool IsRunning,
    bool IsPaused,
    bool IsStopping,
    bool IsConnected,
    PokeRoutineType CurrentRoutine);

public sealed record TradeRuntimeSnapshot(
    ProgramMode GameMode,
    bool IsAvailable,
    bool IsRunning,
    bool IsQueueOpen,
    int QueueCount,
    string Generation,
    IReadOnlyList<TradeBotInstanceSnapshot> Bots);

public sealed record TradeRuntimeResolution(
    TradeRuntimeSnapshot Snapshot,
    IPokeBotRunner? Runner,
    TradeControlError? Error)
{
    public bool IsSuccess => Runner is not null && Error is null;
}

/// <summary>
/// Resolves the active runner for every call. Implementations must not retain a
/// runner across calls because the WinForms mode switch replaces it.
/// </summary>
public interface ICurrentTradeRuntime
{
    TradeRuntimeSnapshot Inspect();

    TradeRuntimeResolution Resolve(
        ProgramMode expectedMode,
        string? expectedGeneration = null,
        bool requireRunning = true,
        bool requireOpenQueue = true);
}

public sealed class CurrentTradeRuntime : ICurrentTradeRuntime
{
    private readonly Func<IPokeBotRunner?> _getRunner;
    private readonly object _sync = new();
    private IPokeBotRunner? _lastRunner;
    private ProgramMode _lastMode;
    private long _generation;

    public CurrentTradeRuntime(Func<IPokeBotRunner?> getRunner)
    {
        _getRunner = getRunner ?? throw new ArgumentNullException(nameof(getRunner));
    }

    public TradeRuntimeSnapshot Inspect()
    {
        lock (_sync)
        {
            var runner = _getRunner();
            var mode = runner?.Config.Distribution.CurrentMode ?? ProgramMode.None;
            if (!ReferenceEquals(runner, _lastRunner) || mode != _lastMode)
            {
                _lastRunner = runner;
                _lastMode = mode;
                _generation++;
            }

            return CreateSnapshot(runner, mode, $"runtime-{_generation}");
        }
    }

    public TradeRuntimeResolution Resolve(
        ProgramMode expectedMode,
        string? expectedGeneration = null,
        bool requireRunning = true,
        bool requireOpenQueue = true)
    {
        var snapshot = Inspect();
        IPokeBotRunner? runner;
        lock (_sync)
            runner = _lastRunner;

        if (!snapshot.IsAvailable || runner is null)
        {
            return new(snapshot, null, Error(
                TradeControlErrorCodes.BotOffline,
                "No current PokeBot runtime is available."));
        }

        if (snapshot.GameMode != expectedMode)
        {
            return new(snapshot, null, Error(
                TradeControlErrorCodes.ModeMismatch,
                $"The current runtime is {snapshot.GameMode}, not {expectedMode}.",
                ("expected_mode", expectedMode.ToString()),
                ("actual_mode", snapshot.GameMode.ToString())));
        }

        if (!string.IsNullOrWhiteSpace(expectedGeneration) &&
            !string.Equals(snapshot.Generation, expectedGeneration, StringComparison.Ordinal))
        {
            return new(snapshot, null, Error(
                TradeControlErrorCodes.ModeMismatch,
                "The current runtime changed after the plan was validated.",
                ("expected_generation", expectedGeneration),
                ("actual_generation", snapshot.Generation)));
        }

        if (requireRunning && !snapshot.IsRunning)
        {
            return new(snapshot, null, Error(
                TradeControlErrorCodes.BotOffline,
                "The current PokeBot runtime is not running."));
        }

        if (requireOpenQueue && !snapshot.IsQueueOpen)
        {
            return new(snapshot, null, Error(
                TradeControlErrorCodes.QueueClosed,
                "The current trade queue is closed or has no ready trade bot."));
        }

        return new(snapshot, runner, null);
    }

    private static TradeRuntimeSnapshot CreateSnapshot(
        IPokeBotRunner? runner,
        ProgramMode mode,
        string generation)
    {
        if (runner is null)
        {
            return new(
                ProgramMode.None,
                false,
                false,
                false,
                0,
                generation,
                []);
        }

        var bots = new List<TradeBotInstanceSnapshot>(runner.Bots.Count);
        for (int i = 0; i < runner.Bots.Count; i++)
        {
            var source = runner.Bots[i];
            var bot = source.Bot;
            var connectionName = bot.Connection.Name;
            bots.Add(new(
                CreateBotInstanceId(connectionName, i),
                connectionName,
                source.IsRunning,
                source.IsPaused,
                source.IsStopping,
                bot.Connection.Connected,
                bot.Config.CurrentRoutineType));
        }

        var (queueOpen, queueCount) = GetQueueState(runner, mode);
        return new(
            mode,
            true,
            runner.IsRunning,
            queueOpen,
            queueCount,
            generation,
            bots);
    }

    private static (bool Open, int Count) GetQueueState(
        IPokeBotRunner runner,
        ProgramMode mode) => mode switch
        {
            ProgramMode.SWSH when runner is PokeBotRunner<PKHeX.Core.PK8> typed =>
                (typed.Hub.Queues.Info.GetCanQueue(), typed.Hub.Queues.Info.Count),
            ProgramMode.BDSP when runner is PokeBotRunner<PKHeX.Core.PB8> typed =>
                (typed.Hub.Queues.Info.GetCanQueue(), typed.Hub.Queues.Info.Count),
            ProgramMode.LA when runner is PokeBotRunner<PKHeX.Core.PA8> typed =>
                (typed.Hub.Queues.Info.GetCanQueue(), typed.Hub.Queues.Info.Count),
            ProgramMode.SV when runner is PokeBotRunner<PKHeX.Core.PK9> typed =>
                (typed.Hub.Queues.Info.GetCanQueue(), typed.Hub.Queues.Info.Count),
            ProgramMode.LGPE when runner is PokeBotRunner<PKHeX.Core.PB7> typed =>
                (typed.Hub.Queues.Info.GetCanQueue(), typed.Hub.Queues.Info.Count),
            ProgramMode.LZA when runner is PokeBotRunner<PKHeX.Core.PA9> typed =>
                (typed.Hub.Queues.Info.GetCanQueue(), typed.Hub.Queues.Info.Count),
            _ => (false, 0),
        };

    private static string CreateBotInstanceId(string name, int index)
    {
        var safe = string.IsNullOrWhiteSpace(name) ? "switch" : name.Trim();
        return $"{safe}:{index}";
    }

    private static TradeControlError Error(
        string code,
        string message,
        params (string Key, object? Value)[] details)
    {
        Dictionary<string, object?>? values = null;
        if (details.Length > 0)
        {
            values = [];
            foreach (var (key, value) in details)
                values[key] = value;
        }

        return new(code, message, values);
    }
}
