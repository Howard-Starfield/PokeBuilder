using FluentAssertions;
using PKHeX.Core;
using SysBot.Pokemon.Helpers;
using Xunit;

namespace SysBot.Tests;

public sealed class PokeBuilderBrandingTests
{
    [Fact]
    public void RepositoryEndpoints_TargetPokeBuilder()
    {
        PokeBot.RepositoryOwner.Should().Be("Howard-Starfield");
        PokeBot.RepositoryName.Should().Be("PokeBuilder");
        PokeBot.RepositoryUrl.Should().Be("https://github.com/Howard-Starfield/PokeBuilder");
        PokeBot.ReleasesUrl.Should().Be("https://github.com/Howard-Starfield/PokeBuilder/releases");
        PokeBot.LatestReleaseApiUrl.Should().Be(
            "https://api.github.com/repos/Howard-Starfield/PokeBuilder/releases/latest");
        PokeBot.Attribution.Should().Be(PokeBot.RepositoryUrl);
    }

    [Theory]
    [InlineData((ushort)Species.Pikachu, false, "Non-Shiny/pikachu.png")]
    [InlineData((ushort)Species.NidoranF, false, "Non-Shiny/nidoran-f.png")]
    [InlineData((ushort)Species.MrMime, false, "Non-Shiny/mr.-mime.png")]
    [InlineData((ushort)Species.Pikachu, true, "Shiny/pikachu.png")]
    public void PokemonImages_UseHowardOwnedSpriteRepository(
        ushort species,
        bool shiny,
        string expectedPath)
    {
        var pk = new PK9
        {
            Species = species,
            TID16 = 0,
            SID16 = 0,
            PID = shiny ? 0u : 0x1000_0000u,
        };

        string url = TradeExtensions<PK9>.PokeImg(pk, canGmax: false, fullSize: true);

        url.Should().Be($"https://raw.githubusercontent.com/Howard-Starfield/sprites/main/{expectedPath}");
    }

    [Fact]
    public void GigantamaxImage_UsesHowardOwnedFormSprite()
    {
        var pk = new PK8
        {
            Species = (ushort)Species.Charizard,
            TID16 = 0,
            SID16 = 0,
            PID = 0x1000_0000,
        };

        string url = TradeExtensions<PK8>.PokeImg(pk, canGmax: true, fullSize: true);

        url.Should().Be(
            "https://raw.githubusercontent.com/Howard-Starfield/sprites/main/Non-Shiny/charizard-Gigantamax.png");
    }
}
