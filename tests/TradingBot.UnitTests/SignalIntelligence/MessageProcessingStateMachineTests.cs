using System;
using FluentAssertions;
using TradingBot.Domain.Exceptions;
using TradingBot.Domain.SignalIntelligence.Entities;
using Xunit;

namespace TradingBot.UnitTests.SignalIntelligence;

public class MessageProcessingStateMachineTests
{
    [Fact]
    public void Tracker_ShouldInitializeWithReceivedState()
    {
        // Arrange & Act
        var tracker = new MessageProcessingTracker(Guid.NewGuid(), "RECEIVED");

        // Assert
        tracker.State.Should().Be("RECEIVED");
    }

    [Fact]
    public void Tracker_ShouldPreventInvalidTransitions()
    {
        // Arrange
        var tracker = new MessageProcessingTracker(Guid.NewGuid(), "RECEIVED");

        // Act & Assert
        // RECEIVED -> VALIDATED is invalid directly
        var act = () => tracker.TransitionTo("VALIDATED");
        act.Should().Throw<DomainException>().WithMessage("*Invalid transition*");
    }

    [Fact]
    public void Tracker_ShouldPreventTransitioningFromTerminalPublishedState()
    {
        // Arrange
        var tracker = new MessageProcessingTracker(Guid.NewGuid(), "RECEIVED");
        tracker.TransitionTo("PROCESSING");
        tracker.TransitionTo("ANALYZED");
        tracker.TransitionTo("VALIDATED");
        tracker.TransitionTo("PUBLISHED");

        // Act & Assert
        var act = () => tracker.TransitionTo("PROCESSING");
        act.Should().Throw<DomainException>().WithMessage("*Cannot transition from terminal state*");
    }

    [Fact]
    public void Tracker_ShouldAllowTransitionToFailedFromActiveStates()
    {
        // Arrange
        var tracker = new MessageProcessingTracker(Guid.NewGuid(), "RECEIVED");
        tracker.TransitionTo("PROCESSING");

        // Act
        tracker.TransitionTo("FAILED");

        // Assert
        tracker.State.Should().Be("FAILED");
    }
}
