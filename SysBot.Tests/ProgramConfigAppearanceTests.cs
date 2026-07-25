using FluentAssertions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace SysBot.Pokemon.Tests;

public class ProgramConfigAppearanceTests
{
    [Fact]
    public void ConfigurationFontScale_RoundTripsThroughConfigJson()
    {
        var config = new ProgramConfig { ConfigurationFontScalePercent = 150 };

        var json = JsonSerializer.Serialize(config, ProgramConfigContext.Default.ProgramConfig);
        var restored = JsonSerializer.Deserialize(json, ProgramConfigContext.Default.ProgramConfig);

        restored.Should().NotBeNull();
        restored!.ConfigurationFontScalePercent.Should().Be(150);
    }

    [Fact]
    public void ConfigurationFontScale_UsesReadableDefaultWhenMissingFromOlderConfig()
    {
        var json = JsonSerializer.Serialize(new ProgramConfig(), ProgramConfigContext.Default.ProgramConfig);
        var oldConfig = JsonNode.Parse(json)!.AsObject();
        oldConfig.Remove(nameof(ProgramConfig.ConfigurationFontScalePercent));

        var restored = JsonSerializer.Deserialize(
            oldConfig.ToJsonString(),
            ProgramConfigContext.Default.ProgramConfig);

        restored.Should().NotBeNull();
        restored!.ConfigurationFontScalePercent.Should().Be(ProgramConfig.DefaultConfigurationFontScalePercent);
    }

    [Theory]
    [InlineData(50, ProgramConfig.MinConfigurationFontScalePercent)]
    [InlineData(250, ProgramConfig.MaxConfigurationFontScalePercent)]
    public void Normalize_ClampsConfigurationFontScale(int requested, int expected)
    {
        var config = new ProgramConfig { ConfigurationFontScalePercent = requested };

        ProgramConfigPersistence.Normalize(config).Should().BeTrue();

        config.ConfigurationFontScalePercent.Should().Be(expected);
    }
}
