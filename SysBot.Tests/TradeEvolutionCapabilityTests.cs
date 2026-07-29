using FluentAssertions;
using Microsoft.Data.Sqlite;
using PKHeX.Core;
using SysBot.Pokemon;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace SysBot.Tests;

public sealed class TradeEvolutionCapabilityTests
{
    public static TheoryData<ProgramMode> SupportedModes => new()
    {
        ProgramMode.SWSH,
        ProgramMode.BDSP,
        ProgramMode.LA,
        ProgramMode.SV,
        ProgramMode.LGPE,
        ProgramMode.LZA,
    };

    [Theory]
    [MemberData(nameof(SupportedModes))]
    public void EveryMode_HasPreConfirmBlockParity_ButNoUnprovenAllowPolicy(
        ProgramMode mode)
    {
        var registry = new TradeEvolutionCapabilityRegistry();
        var capability = registry.Get(mode);

        capability.OrdinaryPreConfirmDetection.Should().BeTrue();
        capability.BatchPreConfirmDetection.Should().BeTrue();
        capability.NativeSwitchValidated.Should().BeFalse();
        capability.EvolutionAnimationHandled.Should().BeFalse();
        capability.MoveLearningHandled.Should().BeFalse();
        registry.Supports(mode, TradeEvolutionPolicy.Block).Should().BeTrue();
        registry.Supports(mode, TradeEvolutionPolicy.AllowManual).Should().BeFalse();
        registry.Supports(mode, TradeEvolutionPolicy.AllowAndHandle).Should().BeFalse();
        capability.Evidence.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [MemberData(nameof(SupportedModes))]
    public void Validation_RejectsNonBlockPolicy_WithCapabilityEvidence(
        ProgramMode mode)
    {
        using var database = new TemporaryDatabase();
        var service = database.CreateService();
        var access = mode == ProgramMode.LGPE
            ? """{"pictocodes":["Pikachu","Eevee","Bulbasaur"]}"""
            : """{"link_code":"13333333"}""";
        var validation = service.Validate(new(
            "owner-a",
            mode,
            access,
            new TradePlanPolicies
            {
                Evolution = TradeEvolutionPolicy.AllowAndHandle,
            },
            [new("one", "Pikachu\nLevel: 50")],
            $"validate-evolution-{mode}"));

        validation.IsValid.Should().BeFalse();
        var error = validation.Errors.Single(z =>
            z.Code == TradeControlErrorCodes.EvolutionRequiresAttention);
        error.Details!["native_switch_validated"].Should().Be(false);
        error.Details["batch_preconfirm_detection"].Should().Be(true);
        error.Details["evidence"].Should().NotBeNull();
    }

    [Fact]
    public void ControlPlaneDetail_ContextEnablesBlockWithoutAffectingLegacyEntries()
    {
        var legacy = new PokeTradeDetail<PK9>(
            new PK9(),
            new("legacy"),
            PokeTradeHub<PK9>.LogNotifier,
            PokeTradeType.Specific,
            13333333);
        legacy.RequiresControlPlaneEvolutionBlock().Should().BeFalse();

        legacy.Context[TradeControlContextKeys.EvolutionPolicy] =
            TradeEvolutionPolicy.Block.ToString();
        legacy.RequiresControlPlaneEvolutionBlock().Should().BeTrue();
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"pokebot-evolution-tests-{Guid.NewGuid():N}");

        public TemporaryDatabase() => Directory.CreateDirectory(_directory);

        public TradePlanApplicationService CreateService()
        {
            var store = new SqliteTradePlanStore(
                System.IO.Path.Combine(_directory, "trade.sqlite3"));
            store.Initialize();
            return new(
                store,
                new SystemTradeControlClock(),
                new Uuid7TradeControlIdGenerator());
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
    }
}
