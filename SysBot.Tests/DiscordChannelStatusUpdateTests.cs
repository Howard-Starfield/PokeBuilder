using FluentAssertions;
using SysBot.Pokemon.Discord;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace SysBot.Tests;

public class DiscordChannelStatusUpdateTests
{
    [Fact]
    public async Task ApplyAsync_WhenStatusChangesDuringUpdate_AppliesLatestStatusLast()
    {
        var update = new LatestStatusUpdate();
        var firstUpdateStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var appliedStatuses = new List<string>();

        update.SetDesired("Offline");
        var applyUpdate = update.ApplyAsync(async (status, attempt, token) =>
        {
            appliedStatuses.Add(status);
            if (status == "Offline")
            {
                firstUpdateStarted.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            }

            return true;
        });

        await firstUpdateStarted.Task;
        update.SetDesired("Online");
        await applyUpdate;

        appliedStatuses.Should().Equal("Offline", "Online");
    }

    [Fact]
    public async Task ApplyAsync_WhenStillIncorrect_TriesTwiceThenStops()
    {
        var update = new LatestStatusUpdate();
        var attempts = new List<int>();
        var delays = new List<TimeSpan>();

        update.SetDesired("Online");
        bool applied = await update.ApplyAsync(
            (status, attempt, token) =>
            {
                attempts.Add(attempt);
                return Task.FromResult(false);
            },
            (delay, token) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        applied.Should().BeFalse();
        attempts.Should().Equal(1, 2);
        delays.Should().Equal(LatestStatusUpdate.RetryDelay);
    }

    [Fact]
    public async Task ApplyAsync_WhenSecondScanIsCorrect_StopsWithoutThirdAttempt()
    {
        var update = new LatestStatusUpdate();
        var attempts = new List<int>();

        update.SetDesired("Online");
        bool applied = await update.ApplyAsync(
            (status, attempt, token) =>
            {
                attempts.Add(attempt);
                return Task.FromResult(attempt == 2);
            },
            (delay, token) => Task.CompletedTask);

        applied.Should().BeTrue();
        attempts.Should().Equal(1, 2);
    }

    [Theory]
    [InlineData("❌trades", "✅", "✅trades")]
    [InlineData("✅❌trades", "❌", "❌trades")]
    [InlineData("🟢-trades", "✅", "✅trades")]
    [InlineData("🔴・trades", "✅", "✅trades")]
    [InlineData("📣announcements", "✅", "✅📣announcements")]
    public void Apply_ReconcilesLiveNameToExactlyOneStatusEmoji(
        string currentName,
        string desiredEmoji,
        string expected)
    {
        DiscordChannelStatusName.Apply(currentName, desiredEmoji, "✅", "❌")
            .Should().Be(expected);
    }

    [Fact]
    public void Apply_RemovesPreviouslyConfiguredCustomStatusEmoji()
    {
        DiscordChannelStatusName.Apply("🔵trades", "🟣", "🟣", "🔵")
            .Should().Be("🟣trades");
    }

    [Fact]
    public void GetCurrent_WhenConnectionEventWasMissed_UsesLiveRunnerState()
    {
        DiscordBotStatus.GetCurrent([(true, true)])
            .Should().Be("Online");
    }

    [Theory]
    [MemberData(nameof(OfflineBotStates))]
    public void GetCurrent_WhenNoRunningBotConnectionIsActive_ReturnsOffline(
        (bool IsRunning, bool IsConnected)[] botStates)
    {
        DiscordBotStatus.GetCurrent(botStates).Should().Be("Offline");
    }

    public static TheoryData<(bool IsRunning, bool IsConnected)[]> OfflineBotStates => new()
    {
        new (bool IsRunning, bool IsConnected)[] { },
        new (bool IsRunning, bool IsConnected)[] { (false, false) },
        new (bool IsRunning, bool IsConnected)[] { (true, false) },
        new (bool IsRunning, bool IsConnected)[] { (false, true) },
    };
}
