using FluentAssertions;
using SysBot.Pokemon;
using SysBot.Pokemon.Discord;
using System.Drawing;
using Xunit;

namespace SysBot.Tests;

public class TradeEmbedImageSizerTests
{
    [Theory]
    [InlineData(TradeSettings.ImageSize.Size128x128, 128)]
    [InlineData(TradeSettings.ImageSize.Size256x256, 256)]
    public void Resize_UsesConfiguredSpeciesImageSize(TradeSettings.ImageSize imageSize, int expectedPixels)
    {
#pragma warning disable CA1416 // Tests run against the Windows PokeBot UI target.
        using var source = new Bitmap(512, 256);
        using var resized = TradeEmbedImageSizer.Resize(source, imageSize);

        resized.Width.Should().Be(expectedPixels);
        resized.Height.Should().Be(expectedPixels);
#pragma warning restore CA1416
    }
}
