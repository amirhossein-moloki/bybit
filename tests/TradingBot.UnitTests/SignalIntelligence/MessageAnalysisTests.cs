using System;
using FluentAssertions;
using TradingBot.Domain.Exceptions;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Domain.SignalIntelligence.Enums;
using Xunit;

namespace TradingBot.UnitTests.SignalIntelligence;

public class MessageAnalysisTests
{
    [Fact]
    public void Constructor_ShouldCreateMessageAnalysis_WhenInputsAreValid()
    {
        // Arrange
        Guid telegramMessageId = Guid.NewGuid();
        MessageType messageType = MessageType.SIGNAL;
        decimal confidence = 0.85m;
        string extractedData = "{\"symbol\": \"BTCUSDT\"}";
        bool aiUsed = true;
        DateTime processedAt = DateTime.UtcNow;

        // Act
        var analysis = new MessageAnalysis(telegramMessageId, messageType, confidence, extractedData, aiUsed, processedAt);

        // Assert
        analysis.Should().NotBeNull();
        analysis.Id.Should().NotBeEmpty();
        analysis.TelegramMessageId.Should().Be(telegramMessageId);
        analysis.MessageType.Should().Be(messageType);
        analysis.Confidence.Should().Be(confidence);
        analysis.ExtractedData.Should().Be(extractedData);
        analysis.AIUsed.Should().Be(aiUsed);
        analysis.ProcessedAt.Should().Be(processedAt);
        analysis.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Constructor_ShouldThrowDomainException_WhenTelegramMessageIdIsEmpty()
    {
        // Act
        Action act = () => new MessageAnalysis(Guid.Empty, MessageType.SIGNAL, 0.9m, "{}", false, DateTime.UtcNow);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("TelegramMessageId is required.");
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.01)]
    [InlineData(5.0)]
    public void Constructor_ShouldThrowDomainException_WhenConfidenceIsOutOfBounds(double invalidConfidence)
    {
        // Act
        Action act = () => new MessageAnalysis(Guid.NewGuid(), MessageType.SIGNAL, (decimal)invalidConfidence, "{}", false, DateTime.UtcNow);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("Confidence must be between 0 and 1.");
    }

    [Fact]
    public void Constructor_ShouldThrowDomainException_WhenMessageTypeIsInvalid()
    {
        // Act
        Action act = () => new MessageAnalysis(Guid.NewGuid(), (MessageType)99, 0.8m, "{}", false, DateTime.UtcNow);

        // Assert
        act.Should().Throw<DomainException>().WithMessage("MessageType is invalid.");
    }

    [Fact]
    public void Constructor_ShouldSetDefaultExtractedData_WhenNullPassed()
    {
        // Arrange
        Guid telegramMessageId = Guid.NewGuid();

        // Act
        var analysis = new MessageAnalysis(telegramMessageId, MessageType.SIGNAL, 0.5m, null!, true, DateTime.UtcNow);

        // Assert
        analysis.ExtractedData.Should().Be("{}");
    }
}
