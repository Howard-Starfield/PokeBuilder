using FluentAssertions;
using PKHeX.Core;
using SysBot.Pokemon.Helpers;
using System.Net;
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

    [Fact]
    public void MissingLatestRelease_IsNotClassifiedAsInternetFailure()
    {
        PokeBotReleaseCheck.ClassifyHttpStatus(HttpStatusCode.NotFound)
            .Should().Be(PokeBotReleaseCheckStatus.NoPublishedRelease);
        PokeBotReleaseCheck.GetFailureMessage(PokeBotReleaseCheckStatus.NoPublishedRelease)
            .Should().Contain("does not have a published GitHub release");
        PokeBotReleaseCheck.ShouldRetry(HttpStatusCode.NotFound).Should().BeFalse();
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public void TransientGitHubFailures_AreRetryable(HttpStatusCode statusCode)
    {
        PokeBotReleaseCheck.ClassifyHttpStatus(statusCode)
            .Should().Be(PokeBotReleaseCheckStatus.ApiError);
        PokeBotReleaseCheck.ShouldRetry(statusCode).Should().BeTrue();
    }

    [Theory]
    [InlineData("v1.3.7", "v1.3.8", true)]
    [InlineData("v1.3.8", "v1.3.8", false)]
    [InlineData("1.3.8", "v1.3.8", false)]
    [InlineData("v1.3.8.0", "v1.3.8", false)]
    [InlineData("v1.3.9", "v1.3.8", false)]
    [InlineData("development", "v1.3.8", false)]
    [InlineData("v1.3.8", "not-a-version", false)]
    public void ReleaseVersionComparison_OnlyOffersStrictlyNewerVersions(
        string currentVersion,
        string repositoryVersion,
        bool expected)
    {
        PokeBotReleaseCheck.IsNewerVersion(repositoryVersion, currentVersion)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void UpdatePolicy_NeverReinstallsTheCurrentRelease(
        bool updateAvailable,
        bool forceRequested,
        bool expected)
    {
        PokeBotReleaseCheck.ShouldInstallUpdate(updateAvailable, forceRequested)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(
        new[] { "PokeBot.exe", @"D:\Bots\primary settings.json" },
        @"D:\Bots\primary settings.json")]
    [InlineData(
        new[] { "PokeBot.exe", "--diagnostic", @"D:\Bots\primary settings.JSON" },
        @"D:\Bots\primary settings.JSON")]
    [InlineData(new[] { "PokeBot.exe", "--diagnostic" }, null)]
    public void LaunchArguments_PreserveTheSelectedJsonConfiguration(
        string[] arguments,
        string? expected)
    {
        PokeBotLaunchArguments.FindConfigPath(arguments).Should().Be(expected);
    }
}
