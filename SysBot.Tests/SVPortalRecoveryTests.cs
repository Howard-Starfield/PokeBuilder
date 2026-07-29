using FluentAssertions;
using SysBot.Pokemon;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SysBot.Tests;

public sealed class SVPortalRecoveryTests
{
    [Fact]
    public async Task ConfirmedExitWaitsQuietlyForPortalTransition()
    {
        var portalStates = new Queue<bool>([true, true, true, true, false]);
        var actions = new List<string>();

        var result = await PokePortalRecovery.TryExitAsync(
            _ => Task.FromResult(portalStates.Dequeue()),
            (delay, _) => Record(actions, $"B:{delay}"),
            (delay, _) => Record(actions, $"A:{delay}"),
            (delay, _) => Record(actions, $"wait:{delay}"),
            CancellationToken.None);

        result.Should().BeTrue();
        actions.Should().Equal(
            $"B:{PokePortalRecovery.PromptSettleDelayMilliseconds}",
            $"A:{PokePortalRecovery.ConfirmationSettleDelayMilliseconds}",
            $"wait:{PokePortalRecovery.TransitionPollIntervalMilliseconds}",
            $"wait:{PokePortalRecovery.TransitionPollIntervalMilliseconds}");
    }

    [Fact]
    public async Task ExitCompletedByBackDoesNotSendConfirmation()
    {
        var portalStates = new Queue<bool>([true, false]);
        var actions = new List<string>();

        var result = await PokePortalRecovery.TryExitAsync(
            _ => Task.FromResult(portalStates.Dequeue()),
            (delay, _) => Record(actions, $"B:{delay}"),
            (delay, _) => Record(actions, $"A:{delay}"),
            (delay, _) => Record(actions, $"wait:{delay}"),
            CancellationToken.None);

        result.Should().BeTrue();
        actions.Should().Equal($"B:{PokePortalRecovery.PromptSettleDelayMilliseconds}");
    }

    [Fact]
    public async Task StuckPortalRetriesOnlyAfterFullQuietWindow()
    {
        var actions = new List<string>();

        var result = await PokePortalRecovery.TryExitAsync(
            _ => Task.FromResult(true),
            (delay, _) => Record(actions, $"B:{delay}"),
            (delay, _) => Record(actions, $"A:{delay}"),
            (delay, _) => Record(actions, $"wait:{delay}"),
            CancellationToken.None);

        result.Should().BeFalse();
        actions.Count(action => action.StartsWith("B:")).Should().Be(PokePortalRecovery.MaxAttempts);
        actions.Count(action => action.StartsWith("A:")).Should().Be(PokePortalRecovery.MaxAttempts);
        actions.Count(action => action.StartsWith("wait:")).Should()
            .Be(PokePortalRecovery.MaxAttempts * PokePortalRecovery.TransitionPollCount);

        var firstConfirmation = actions.IndexOf($"A:{PokePortalRecovery.ConfirmationSettleDelayMilliseconds}");
        var secondBack = actions.LastIndexOf($"B:{PokePortalRecovery.PromptSettleDelayMilliseconds}");
        secondBack.Should().Be(firstConfirmation + PokePortalRecovery.TransitionPollCount + 1,
            "another B must not be sent until the entire quiet transition window has elapsed");
    }

    private static Task Record(List<string> actions, string action)
    {
        actions.Add(action);
        return Task.CompletedTask;
    }
}
