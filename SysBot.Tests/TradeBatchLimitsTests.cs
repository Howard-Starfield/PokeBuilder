using FluentAssertions;
using SysBot.Pokemon;
using SysBot.Pokemon.Helpers;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using Xunit;

namespace SysBot.Tests;

public class TradeBatchLimitsTests
{
    [Theory]
    [InlineData(false, 5)]
    [InlineData(true, int.MaxValue)]
    public void Ceiling_PerRoleTier(bool isFavored, int expected)
        => TradeBatchLimits.GetCeiling(isFavored).Should().Be(expected);

    [Theory]
    // Config unset (<=0): standard users keep the five-Pokemon ceiling;
    // elevated users receive no bot-side numeric ceiling.
    [InlineData(0, false, 5)]
    [InlineData(0, true, int.MaxValue)]
    [InlineData(-1, false, 5)]
    [InlineData(-1, true, int.MaxValue)]
    // A positive operator value tightens either tier.
    [InlineData(2, false, 2)]
    [InlineData(3, true, 3)]
    // Standard users remain capped at five; elevated users may use the configured value.
    [InlineData(5, false, 5)]
    [InlineData(6, false, 5)]
    [InlineData(6, true, 6)]
    [InlineData(25, false, 5)]
    [InlineData(25, true, 25)]
    public void EffectiveMax_IntersectsConfigWithTierCeiling(int configValue, bool isFavored, int expected)
        => TradeBatchLimits.GetEffectiveMax(configValue, isFavored).Should().Be(expected);

    [Fact]
    public void NewConfiguration_DefaultsOperatorCapToUnlimited()
    {
        var maximum = typeof(LegalitySettings).GetProperty("MaxPkmsPerTrade");
        maximum.Should().NotBeNull();
        maximum!.GetValue(new LegalitySettings()).Should().Be(0);
    }

    [Fact]
    public void BatchSettings_AreExposedTogetherUnderLegalityGenerateCategory()
    {
        var allow = typeof(LegalitySettings).GetProperty("AllowBatchTrades");
        var maximum = typeof(LegalitySettings).GetProperty("MaxPkmsPerTrade");

        allow.Should().NotBeNull();
        maximum.Should().NotBeNull();
        allow!.GetCustomAttribute<CategoryAttribute>()!.Category.Should().Be("Generate");
        maximum!.GetCustomAttribute<CategoryAttribute>()!.Category.Should().Be("Generate");
        allow.GetCustomAttribute<DisplayNameAttribute>()!.DisplayName.Should().Be("Allow Batch Trades");
        maximum.GetCustomAttribute<DisplayNameAttribute>()!.DisplayName.Should().StartWith("Maximum Pokémon per Batch");
        var generateProperties = TypeDescriptor.GetProperties(new LegalitySettings())
            .Cast<PropertyDescriptor>()
            .Where(property => property.Category == "Generate")
            .ToList();
        int allowIndex = generateProperties.FindIndex(property => property.Name == "AllowBatchTrades");
        int maximumIndex = generateProperties.FindIndex(property => property.Name == "MaxPkmsPerTrade");
        maximumIndex.Should().Be(allowIndex + 1,
            "the PropertyGrid uses TypeDescriptor order and the cap must appear immediately below batch enable");
    }
}
