using System;
using FluentAssertions;
using SysBot.Pokemon;
using Xunit;

namespace SysBot.Tests;

public class TradeControlContractTests
{
    [Fact]
    public void DefaultPolicies_AreFailClosed()
    {
        var policies = new TradePlanPolicies();

        policies.Evolution.Should().Be(TradeEvolutionPolicy.Block);
        policies.PartnerDisconnectMaxAttempts.Should().Be(3);
        policies.TransportReconnectDelaysMs.Should().Equal(0, 250, 1_000, 5_000, 30_000);
        policies.OnRetryExhausted.Should().Be(TradeRetryExhaustedPolicy.Pause);
        policies.OnUncertainSettlement.Should().Be(TradeUncertainSettlementPolicy.NeedsAttention);
        policies.Validate().Should().BeEmpty();
    }

    [Fact]
    public void InvalidRecoveryPolicies_AreRejected()
    {
        var policies = new TradePlanPolicies
        {
            PartnerDisconnectMaxAttempts = -1,
            TransportReconnectDelaysMs = [0, -1],
        };

        policies.Validate().Should().HaveCount(2);
    }

    [Fact]
    public void PlanHappyPath_IsAllowed()
    {
        TradePlanState.Draft.CanTransitionTo(TradePlanState.Validated).Should().BeTrue();
        TradePlanState.Validated.CanTransitionTo(TradePlanState.Queued).Should().BeTrue();
        TradePlanState.Queued.CanTransitionTo(TradePlanState.Running).Should().BeTrue();
        TradePlanState.Running.CanTransitionTo(TradePlanState.Completed).Should().BeTrue();
    }

    [Theory]
    [InlineData(TradePlanState.Completed)]
    [InlineData(TradePlanState.Failed)]
    [InlineData(TradePlanState.Cancelled)]
    public void TerminalPlanStates_CannotTransition(TradePlanState terminal)
    {
        foreach (var next in Enum.GetValues<TradePlanState>())
            terminal.CanTransitionTo(next).Should().BeFalse();
    }

    [Fact]
    public void ConfirmingItem_CannotAutomaticallyRetry()
    {
        TradePlanItemState.Confirming.CanTransitionTo(TradePlanItemState.Pending).Should().BeFalse();
        TradePlanItemState.Confirming.CanTransitionTo(TradePlanItemState.NeedsAttention).Should().BeTrue();
    }

    [Fact]
    public void AttentionItem_AllowsExplicitOperatorResolution()
    {
        TradePlanItemState.NeedsAttention.CanTransitionTo(TradePlanItemState.Pending).Should().BeTrue();
        TradePlanItemState.NeedsAttention.CanTransitionTo(TradePlanItemState.Completed).Should().BeTrue();
        TradePlanItemState.NeedsAttention.CanTransitionTo(TradePlanItemState.Skipped).Should().BeTrue();
        TradePlanItemState.NeedsAttention.CanTransitionTo(TradePlanItemState.Confirming).Should().BeFalse();
    }
}
