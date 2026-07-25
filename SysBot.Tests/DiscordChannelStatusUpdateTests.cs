using FluentAssertions;
using SysBot.Pokemon.Discord;
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
        var applyUpdate = update.ApplyAsync(async (status, token) =>
        {
            appliedStatuses.Add(status);
            if (status == "Offline")
            {
                firstUpdateStarted.SetResult();
                await Task.Delay(Timeout.Infinite, token);
            }
        });

        await firstUpdateStarted.Task;
        update.SetDesired("Online");
        await applyUpdate;

        appliedStatuses.Should().Equal("Offline", "Online");
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
}
