using FluentAssertions;
using SysBot.Base;
using SysBot.Pokemon;
using System;
using System.Collections.Generic;
using Xunit;

namespace SysBot.Tests;

public sealed class TradeRuntimeAdapterTests
{
    public static TheoryData<ProgramMode> SupportedModes => new()
    {
        ProgramMode.SWSH,
        ProgramMode.BDSP,
        ProgramMode.LA,
        ProgramMode.SV,
        ProgramMode.LGPE,
        ProgramMode.LZA,
    };

    [Theory]
    [MemberData(nameof(SupportedModes))]
    public void Resolve_RechecksCurrentFakeRunner_ForEverySupportedMode(
        ProgramMode mode)
    {
        var runner = new FakeRunner(mode, isRunning: true);
        var runtime = new CurrentTradeRuntime(() => runner);
        var snapshot = runtime.Inspect();

        var result = runtime.Resolve(
            mode,
            snapshot.Generation,
            requireRunning: true,
            requireOpenQueue: false);

        result.IsSuccess.Should().BeTrue();
        result.Runner.Should().BeSameAs(runner);
        result.Snapshot.GameMode.Should().Be(mode);
    }

    [Fact]
    public void Resolve_RejectsStaleGeneration_AfterRunnerReplacement()
    {
        IPokeBotRunner? runner = new FakeRunner(ProgramMode.SV, true);
        var runtime = new CurrentTradeRuntime(() => runner);
        var original = runtime.Inspect();

        runner = new FakeRunner(ProgramMode.SV, true);
        var result = runtime.Resolve(
            ProgramMode.SV,
            original.Generation,
            requireRunning: true,
            requireOpenQueue: false);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(TradeControlErrorCodes.ModeMismatch);
        result.Snapshot.Generation.Should().NotBe(original.Generation);
    }

    [Fact]
    public void Resolve_RejectsModeChange_EvenWhenRunnerObjectIsSame()
    {
        var runner = new FakeRunner(ProgramMode.SV, true);
        var runtime = new CurrentTradeRuntime(() => runner);
        var original = runtime.Inspect();
        runner.Config.Distribution.CurrentMode = ProgramMode.LZA;

        var result = runtime.Resolve(
            ProgramMode.SV,
            original.Generation,
            requireRunning: true,
            requireOpenQueue: false);

        result.IsSuccess.Should().BeFalse();
        result.Error!.Code.Should().Be(TradeControlErrorCodes.ModeMismatch);
        result.Snapshot.GameMode.Should().Be(ProgramMode.LZA);
    }

    [Fact]
    public void Resolve_RejectsMissingAndStoppedRunners()
    {
        var missing = new CurrentTradeRuntime(() => null)
            .Resolve(ProgramMode.SV, requireOpenQueue: false);
        missing.Error!.Code.Should().Be(TradeControlErrorCodes.BotOffline);

        var stoppedRunner = new FakeRunner(ProgramMode.SV, false);
        var stopped = new CurrentTradeRuntime(() => stoppedRunner)
            .Resolve(ProgramMode.SV, requireOpenQueue: false);
        stopped.Error!.Code.Should().Be(TradeControlErrorCodes.BotOffline);
    }

    private sealed class FakeRunner : IPokeBotRunner
    {
        public FakeRunner(ProgramMode mode, bool isRunning)
        {
            Config.Distribution.CurrentMode = mode;
            IsRunning = isRunning;
        }

        public PokeTradeHubConfig Config { get; } = new();

        public bool RunOnce => false;

        public bool IsRunning { get; }

        public IList<BotSource<PokeBotState>> Bots { get; } =
            new List<BotSource<PokeBotState>>();

        public event EventHandler? BotStopped;

        public void StartAll() => throw new NotSupportedException();

        public void StopAll() => throw new NotSupportedException();

        public void InitializeStart() => throw new NotSupportedException();

        public void Shutdown() => throw new NotSupportedException();

        public void Add(PokeRoutineExecutorBase newbot) =>
            throw new NotSupportedException();

        public void Remove(IConsoleBotConfig state, bool callStop) =>
            throw new NotSupportedException();

        public BotSource<PokeBotState>? GetBot(PokeBotState state) => null;

        public PokeRoutineExecutorBase CreateBotFromConfig(PokeBotState cfg) =>
            throw new NotSupportedException();

        public bool SupportsRoutine(PokeRoutineType pokeRoutineType) => true;

        public void RaiseStopped() => BotStopped?.Invoke(this, EventArgs.Empty);
    }
}
