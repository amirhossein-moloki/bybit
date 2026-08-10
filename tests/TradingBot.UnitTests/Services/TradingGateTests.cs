using System;
using FluentAssertions;
using TradingBot.Application.Services;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.UnitTests.Services;

public class TradingGateTests
{
    [Fact]
    public void TradingGate_InitialState_ShouldBeStartingAndDisabled()
    {
        var gate = new TradingGate();
        gate.CurrentState.Should().Be(ApplicationState.Starting);
        gate.IsTradingEnabled.Should().BeFalse();
    }

    [Fact]
    public void SetState_ToNonReadyState_ShouldDisableTrading()
    {
        var gate = new TradingGate();
        gate.SetState(ApplicationState.Initializing);
        gate.CurrentState.Should().Be(ApplicationState.Initializing);
        gate.IsTradingEnabled.Should().BeFalse();
    }

    [Fact]
    public void EnableTrading_WhenReady_ShouldEnableTradingSuccessfully()
    {
        var gate = new TradingGate();
        gate.SetState(ApplicationState.Ready);
        gate.EnableTrading();
        gate.IsTradingEnabled.Should().BeTrue();
    }

    [Fact]
    public void EnableTrading_WhenNotReady_ShouldThrowInvalidOperationException()
    {
        var gate = new TradingGate();
        gate.SetState(ApplicationState.Recovering);
        Action act = () => gate.EnableTrading();
        act.Should().Throw<InvalidOperationException>().WithMessage("*Cannot enable trading*");
    }

    [Fact]
    public void DisableTrading_ShouldForceTradingDisabled()
    {
        var gate = new TradingGate();
        gate.SetState(ApplicationState.Ready);
        gate.EnableTrading();
        gate.IsTradingEnabled.Should().BeTrue();

        gate.DisableTrading();
        gate.IsTradingEnabled.Should().BeFalse();
    }
}
