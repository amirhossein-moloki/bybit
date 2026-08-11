using System;
using FluentAssertions;
using TradingBot.Domain.Exceptions;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Domain.SignalIntelligence.Enums;
using Xunit;

namespace TradingBot.UnitTests.SignalIntelligence;

public class SignalContextTests
{
    [Fact]
    public void Constructor_ShouldCreateSignalContext_WhenInputsAreValid()
    {
        // Arrange
        Guid signalId = Guid.NewGuid();
        long channelId = 123456789;
        string symbol = "BTCUSDT";
        SignalState currentState = SignalState.RECEIVED;
        string lastAction = "Signal Ingested";
        long lastMessageId = 15;

        // Act
        var context = new SignalContext(signalId, channelId, symbol, currentState, lastAction, lastMessageId);

        // Assert
        context.Should().NotBeNull();
        context.Id.Should().NotBeEmpty();
        context.SignalId.Should().Be(signalId);
        context.ChannelId.Should().Be(channelId);
        context.Symbol.Should().Be("BTCUSDT"); // Capitalized
        context.CurrentState.Should().Be(currentState);
        context.LastAction.Should().Be(lastAction);
        context.LastMessageId.Should().Be(lastMessageId);
        context.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        context.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void Constructor_ShouldThrowDomainException_WhenSignalIdIsEmpty()
    {
        // Act
        Action act = () => new SignalContext(Guid.Empty, 123, "BTCUSDT", SignalState.RECEIVED, "action", 1);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("SignalId is required.");
    }

    [Fact]
    public void Constructor_ShouldThrowDomainException_WhenChannelIdIsZero()
    {
        // Act
        Action act = () => new SignalContext(Guid.NewGuid(), 0, "BTCUSDT", SignalState.RECEIVED, "action", 1);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("ChannelId is required.");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Constructor_ShouldThrowDomainException_WhenSymbolIsEmpty(string invalidSymbol)
    {
        // Act
        Action act = () => new SignalContext(Guid.NewGuid(), 123, invalidSymbol, SignalState.RECEIVED, "action", 1);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("Symbol is required.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Constructor_ShouldThrowDomainException_WhenLastMessageIdIsZeroOrNegative(long invalidMessageId)
    {
        // Act
        Action act = () => new SignalContext(Guid.NewGuid(), 123, "BTCUSDT", SignalState.RECEIVED, "action", invalidMessageId);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("LastMessageId must be greater than zero.");
    }

    [Fact]
    public void UpdateState_ShouldModifyStateAndLastActionAndLastMessageId_AndSetUpdatedAt()
    {
        // Arrange
        var context = new SignalContext(Guid.NewGuid(), 123, "BTCUSDT", SignalState.RECEIVED, "Initial", 1);

        // Act
        context.UpdateState(SignalState.ANALYZING, "Analyzing Signal", 2);

        // Assert
        context.CurrentState.Should().Be(SignalState.ANALYZING);
        context.LastAction.Should().Be("Analyzing Signal");
        context.LastMessageId.Should().Be(2);
        context.UpdatedAt.Should().NotBeNull();
        context.UpdatedAt.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void UpdateState_ShouldThrowDomainException_WhenNewStateIsInvalid()
    {
        // Arrange
        var context = new SignalContext(Guid.NewGuid(), 123, "BTCUSDT", SignalState.RECEIVED, "Initial", 1);

        // Act
        Action act = () => context.UpdateState((SignalState)99, "Invalid State", 2);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("NewState is invalid.");
    }

    [Fact]
    public void UpdateState_ShouldThrowDomainException_WhenLastMessageIdIsZeroOrNegative()
    {
        // Arrange
        var context = new SignalContext(Guid.NewGuid(), 123, "BTCUSDT", SignalState.RECEIVED, "Initial", 1);

        // Act
        Action act = () => context.UpdateState(SignalState.ACTIVE, "Active State", 0);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("LastMessageId must be greater than zero.");
    }
}
