using FluentAssertions;
using SysBot.Pokemon.Helpers;
using Xunit;

namespace SysBot.Tests;

public sealed class ConfigurationSearchMatcherTests
{
    [Theory]
    [InlineData("channel whitelist", "Discord Channels Channel Whitelist")]
    [InlineData("whitelst", "Channel Whitelist")]
    [InlineData("chanel whitelst", "Discord Channels Channel Whitelist")]
    [InlineData("anti idle", "General Feature Toggle Anti Idle")]
    [InlineData("log", "Logging Enabled")]
    public void Score_ReturnsMatchForRelatedSettingText(string query, string candidate)
    {
        ConfigurationSearchMatcher.Score(query, candidate).Should().BePositive();
    }

    [Theory]
    [InlineData("", "Channel Whitelist")]
    [InlineData("payment", "Channel Whitelist")]
    [InlineData("xy", "Logging Enabled")]
    public void Score_RejectsUnrelatedText(string query, string candidate)
    {
        ConfigurationSearchMatcher.Score(query, candidate).Should().Be(0);
    }

    [Fact]
    public void Score_RanksExactTitleAboveDescriptionOnlyMatch()
    {
        var exact = ConfigurationSearchMatcher.Score("channel whitelist", "Channel Whitelist");
        var description = ConfigurationSearchMatcher.Score(
            "channel whitelist",
            "Restrict commands to a configured channel whitelist");

        exact.Should().BeGreaterThan(description);
    }
}
