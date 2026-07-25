using System;
using FluentAssertions;
using PKHeX.Core;
using PKHeX.Core.AutoMod;
using SysBot.Pokemon;
using SysBot.Pokemon.Helpers;
using Xunit;

namespace SysBot.Tests;

/// <summary>
/// Focused compatibility gates for the matched PKHeX/AutoMod runtime used by SysBot.
/// These tests do not connect to Discord, a console, or any network service.
/// </summary>
public class AutoModUpgradeTests
{
    private const string BaselineRequest = """
        Pikachu
        Level: 100
        Ball: Poke Ball
        Timid Nature
        - Thunderbolt
        """;

    private const string LegendsArceusRequest = """
        Pikachu
        Level: 100
        Timid Nature
        - Quick Attack
        """;

    static AutoModUpgradeTests() => AutoLegalityWrapper.EnsureInitialized(new Pokemon.LegalitySettings());

    [Fact]
    public void Pkhex_and_automod_are_the_same_expected_release()
    {
        var core = typeof(PKM).Assembly.GetName().Version;
        var autoMod = typeof(APILegality).Assembly.GetName().Version;

        core.Should().Be(new Version(26, 7, 7, 0));
        autoMod.Should().Be(core, "AutoMod must be rebuilt for the exact PKHeX.Core release loaded by SysBot");
    }

    [Fact]
    public void Scarlet_and_violet_mode_generates_a_legal_pk9() => VerifyDestination<PK9>();

    [Fact]
    public void Legends_za_mode_generates_a_legal_pa9() => VerifyDestination<PA9>();

    [Fact]
    public void Bdsp_mode_generates_a_legal_pb8() => VerifyDestination<PB8>();

    [Fact]
    public void Sword_and_shield_mode_generates_a_legal_pk8() => VerifyDestination<PK8>();

    [Fact]
    public void Legends_arceus_mode_generates_a_legal_pa8() => VerifyDestination<PA8>(LegendsArceusRequest);

    [Fact]
    public void Ditto_trade_aligns_effective_nature_with_stored_nature()
    {
        var ditto = new PK9
        {
            Species = (ushort)Species.Ditto,
            Nature = Nature.Jolly,
            StatAlignment = Nature.Modest,
        };

        TradeExtensions<PK9>.DittoTrade(ditto);

        ditto.StatAlignment.Should().Be(Nature.Jolly);
    }

    private static void VerifyDestination<T>(string request = BaselineRequest) where T : PKM, new()
    {
        ShowdownParsing.TryParseAnyLanguage(request, out var set).Should().BeTrue();
        set.Should().NotBeNull();

        var trainer = AutoLegalityWrapper.GetTrainerInfo<T>();
        var template = AutoLegalityWrapper.GetTemplate(set!);
        set.InvalidLines.Should().BeEmpty("RegenTemplate must consume recognized AutoMod extension lines before SysBot rejects unknown input");
        var generated = trainer.GetLegal(template, out var result);

        result.Should().Be("Regenerated");
        generated.Should().BeOfType<T>();

        var legality = new LegalityAnalysis(generated);
        legality.Valid.Should().BeTrue(legality.Report());
    }
}
