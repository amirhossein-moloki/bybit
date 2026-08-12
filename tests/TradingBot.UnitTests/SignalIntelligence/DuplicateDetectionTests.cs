using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Application.SignalIntelligence.Validation;
using TradingBot.Application.Repositories;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Domain.SignalIntelligence.Enums;
using TradingBot.Parser.Parsers;
using Xunit;

namespace TradingBot.UnitTests.SignalIntelligence;

public class DuplicateDetectionTests
{
    private readonly Mock<IMessageAnalysisRepository> _analysisRepoMock = new();
    private readonly Mock<IMessageRepository> _messageRepoMock = new();
    private readonly Mock<IIntelligenceEventPublisher> _eventPublisherMock = new();
    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly Mock<IMessagePreprocessor> _preprocessorMock = new();
    private readonly Mock<IMessageClassifier> _classifierMock = new();
    private readonly MessageParser _parser;

    public DuplicateDetectionTests()
    {
        _parser = new MessageParser(
            _preprocessorMock.Object,
            _classifierMock.Object,
            _analysisRepoMock.Object,
            _messageRepoMock.Object,
            _eventPublisherMock.Object,
            _uowMock.Object,
            NullLogger<MessageParser>.Instance
        );
    }

    [Fact]
    public async Task ParseAsync_WithSameChannelAndMessageId_ShouldPreventDuplicateProcessing()
    {
        // Arrange
        var message1 = new TelegramMessage(100L, 200L, null, "Signal message content", DateTime.UtcNow);
        var message2 = new TelegramMessage(100L, 200L, null, "Signal message content duplicate", DateTime.UtcNow);

        // Mock existing message find
        _messageRepoMock
            .Setup(r => r.GetByChannelMessageIdAsync(100L, 200L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(message1);

        var existingAnalysis = new MessageAnalysis(
            message1.Id,
            MessageType.SIGNAL,
            0.95m,
            "{\"type\":\"SIGNAL\",\"symbol\":\"BTCUSDT\",\"side\":\"Buy\",\"confidence\":0.95}",
            false,
            DateTime.UtcNow
        );

        _analysisRepoMock
            .Setup(r => r.GetByMessageIdAsync(message1.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingAnalysis);

        // Act
        var result = await _parser.ParseAsync(message2);

        // Assert
        result.Should().NotBeNull();
        result.Type.Should().Be(MessageType.SIGNAL);
        result.Symbol.Should().Be("BTCUSDT");

        // Verify that no new analysis or message processed status is written
        _analysisRepoMock.Verify(r => r.CreateAsync(It.IsAny<MessageAnalysis>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
