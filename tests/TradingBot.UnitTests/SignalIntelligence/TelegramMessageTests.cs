using System;
using FluentAssertions;
using TradingBot.Domain.Exceptions;
using TradingBot.Domain.SignalIntelligence.Entities;
using Xunit;

namespace TradingBot.UnitTests.SignalIntelligence;

public class TelegramMessageTests
{
    [Fact]
    public void Constructor_ShouldCreateTelegramMessage_WhenInputsAreValid()
    {
        // Arrange
        long channelId = 123456789;
        long messageId = 1;
        long? senderId = 987654321;
        string content = "This is a trading signal message";
        DateTime receivedAt = DateTime.UtcNow;

        // Act
        var message = new TelegramMessage(channelId, messageId, senderId, content, receivedAt);

        // Assert
        message.Should().NotBeNull();
        message.Id.Should().NotBeEmpty();
        message.ChannelId.Should().Be(channelId);
        message.MessageId.Should().Be(messageId);
        message.SenderId.Should().Be(senderId);
        message.Content.Should().Be(content);
        message.ReceivedAt.Should().Be(receivedAt);
        message.Processed.Should().BeFalse();
        message.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void Constructor_ShouldThrowDomainException_WhenContentIsEmpty(string invalidContent)
    {
        // Act
        Action act = () => new TelegramMessage(123, 1, 456, invalidContent, DateTime.UtcNow);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("Content cannot be empty.");
    }

    [Fact]
    public void Constructor_ShouldThrowDomainException_WhenChannelIdIsZero()
    {
        // Act
        Action act = () => new TelegramMessage(0, 1, 456, "valid content", DateTime.UtcNow);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("ChannelId is required.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Constructor_ShouldThrowDomainException_WhenMessageIdIsZeroOrNegative(long invalidMessageId)
    {
        // Act
        Action act = () => new TelegramMessage(123, invalidMessageId, 456, "valid content", DateTime.UtcNow);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("MessageId is required.");
    }

    [Fact]
    public void MarkProcessed_ShouldSetProcessedToTrue()
    {
        // Arrange
        var message = new TelegramMessage(123, 1, 456, "content", DateTime.UtcNow);

        // Act
        message.MarkProcessed();

        // Assert
        message.Processed.Should().BeTrue();
    }
}
