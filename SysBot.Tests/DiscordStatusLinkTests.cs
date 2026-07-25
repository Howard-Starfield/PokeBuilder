using FluentAssertions;
using SysBot.Pokemon;
using System.Text.Json;
using Xunit;

namespace SysBot.Tests;

public class DiscordStatusLinkTests
{
    [Fact]
    public void DiscordSettings_DefaultStatusUrl_PointsToHowardLab()
    {
        var settings = new DiscordSettings();

        settings.BotStatusUrl.Should().Be("https://resume.howardlab.dev/");
    }

    [Fact]
    public void DiscordSettings_StatusUrl_RoundTripsThroughConfigJson()
    {
        var config = new ProgramConfig();
        config.Hub.Discord.BotStatusUrl = "https://example.com/status";

        var json = JsonSerializer.Serialize(config, ProgramConfigContext.Default.ProgramConfig);
        var restored = JsonSerializer.Deserialize(json, ProgramConfigContext.Default.ProgramConfig);

        restored!.Hub.Discord.BotStatusUrl.Should().Be("https://example.com/status");
    }
}
