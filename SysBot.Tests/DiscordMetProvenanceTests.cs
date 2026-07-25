using FluentAssertions;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using SysBot.Pokemon;
using SysBot.Pokemon.Discord;
using System.Threading.Tasks;
using Xunit;

namespace SysBot.Tests;

public sealed class DiscordMetProvenanceTests
{
    static DiscordMetProvenanceTests() => AutoLegalityWrapper.EnsureInitialized(new SysBot.Pokemon.LegalitySettings
    {
        AllowBatchCommands = true,
    });

    [Theory]
    [InlineData(GameVersion.BD, 64)]
    [InlineData(GameVersion.SP, 66)]
    public async Task Discord_showdown_path_preserves_legal_gengar_turnback_cave_provenance(
        GameVersion trainerVersion,
        byte metLevel)
    {
        var previous = APILegality.AllowBatchCommands;
        APILegality.AllowBatchCommands = true;
        try
        {
            var request = $"Gengar\nLevel: {metLevel}\n.MetLocation=283\n.MetLevel={metLevel}";
            var policy = new ShowdownProcessingPolicy(
                (byte)LanguageID.English,
                DefaultHeldItem: 0,
                EnableSpamCheck: false,
                TrainerVersion: trainerVersion);

            var result = await Helpers<PB8>.ProcessShowdownSetWithPolicyAsync(request, policy);

            result.Error.Should().BeNull();
            result.Pokemon.Should().NotBeNull();
            result.Pokemon!.Species.Should().Be((ushort)Species.Gengar);
            result.Pokemon.MetLocation.Should().Be(283);
            result.Pokemon.MetLevel.Should().Be(metLevel);
            result.Pokemon.CurrentLevel.Should().Be(metLevel);
            new LegalityAnalysis(result.Pokemon).Valid.Should().BeTrue();
        }
        finally
        {
            APILegality.AllowBatchCommands = previous;
        }
    }

    [Theory]
    [InlineData(54, 16)]
    [InlineData(54, 27)]
    [InlineData(57, 16)]
    [InlineData(57, 27)]
    [InlineData(79, 15)]
    [InlineData(79, 26)]
    [InlineData(87, 16)]
    [InlineData(87, 27)]
    [InlineData(94, 15)]
    [InlineData(94, 26)]
    [InlineData(202, 40)]
    [InlineData(202, 42)]
    [InlineData(205, 9)]
    [InlineData(205, 21)]
    [InlineData(207, 40)]
    [InlineData(207, 50)]
    [InlineData(231, 40)]
    [InlineData(231, 42)]
    [InlineData(235, 34)]
    [InlineData(235, 36)]
    [InlineData(235, 45)]
    [InlineData(235, 47)]
    [InlineData(273, 15)]
    [InlineData(273, 22)]
    [InlineData(273, 24)]
    [InlineData(273, 25)]
    [InlineData(273, 31)]
    [InlineData(273, 34)]
    [InlineData(273, 38)]
    [InlineData(273, 41)]
    [InlineData(273, 47)]
    [InlineData(273, 50)]
    public async Task Discord_za_path_accepts_each_gengar_same_location_minimum_level(
        ushort metLocation,
        byte metLevel)
    {
        var previous = APILegality.AllowBatchCommands;
        APILegality.AllowBatchCommands = true;
        try
        {
            var isAlpha = (metLocation, metLevel) is
                (205, 21) or (207, 50) or (235, 45) or (235, 47) or
                (273, 31) or (273, 34) or (273, 47) or (273, 50);
            var alphaLines = isAlpha
                ? $"\nIVs: 31 HP / 31 Atk / 31 Def / 31 SpA / 31 SpD / 31 Spe\nAlpha: Yes\n.ObedienceLevel={metLevel}"
                : string.Empty;
            var request =
                $"Gengar\nLevel: 100\n.MetLocation={metLocation}\n.MetLevel={metLevel}{alphaLines}";
            var policy = new ShowdownProcessingPolicy(
                (byte)LanguageID.English,
                DefaultHeldItem: 0,
                EnableSpamCheck: false,
                TrainerVersion: GameVersion.ZA);

            var result = await Helpers<PA9>.ProcessShowdownSetWithPolicyAsync(request, policy);

            if (result.Error is not null)
            {
                var template = AutoLegalityWrapper.GetTemplate(new ShowdownSet(request));
                var trainer = TrainerSettings.GetSavedTrainerData(GameVersion.ZA, LanguageID.English);
                var generated = trainer.GetLegal(template, out var legalizationResult);
                var legality = new LegalityAnalysis(generated);
                Assert.Fail(
                    $"Discord={result.Error}; ALM={legalizationResult}; " +
                    $"Met={generated.MetLocation}/{generated.MetLevel}; " +
                    $"Obedience={(generated as PA9)?.ObedienceLevel}; {legality.Report()}");
            }
            result.Error.Should().BeNull();
            result.Pokemon.Should().NotBeNull();
            result.Pokemon!.Species.Should().Be((ushort)Species.Gengar);
            result.Pokemon.MetLocation.Should().Be(metLocation);
            result.Pokemon.MetLevel.Should().Be(metLevel);
            result.Pokemon.CurrentLevel.Should().Be(100);
            new LegalityAnalysis(result.Pokemon).Valid.Should().BeTrue();
        }
        finally
        {
            APILegality.AllowBatchCommands = previous;
        }
    }

    [Fact]
    public async Task Discord_sw_path_preserves_gigantamax_request()
    {
        const string request = "Charizard\nLevel: 100\nGigantamax: Yes";
        var policy = new ShowdownProcessingPolicy(
            (byte)LanguageID.English,
            DefaultHeldItem: 0,
            EnableSpamCheck: false,
            TrainerVersion: GameVersion.SW);

        var result = await Helpers<PK8>.ProcessShowdownSetWithPolicyAsync(request, policy);

        result.Error.Should().BeNull();
        result.Pokemon.Should().NotBeNull();
        result.Pokemon!.Species.Should().Be((ushort)Species.Charizard);
        result.Pokemon.CanGigantamax.Should().BeTrue();
        new LegalityAnalysis(result.Pokemon).Valid.Should().BeTrue();
    }
}
