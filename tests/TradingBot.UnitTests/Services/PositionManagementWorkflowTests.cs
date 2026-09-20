using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Repositories;
using TradingBot.Application.Services;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Domain.SignalIntelligence.Enums;
using Xunit;

namespace TradingBot.UnitTests.Services;

public class PositionManagementWorkflowTests
{
    private readonly Mock<IPositionRepository> _positionRepoMock = new();
    private readonly Mock<IStopLossManager> _stopLossManagerMock = new();
    private readonly Mock<IBreakEvenManager> _breakEvenManagerMock = new();
    private readonly Mock<IPartialCloseManager> _partialCloseManagerMock = new();
    private readonly Mock<IPositionCloseManager> _positionCloseManagerMock = new();
    private readonly Mock<IMessageAnalysisRepository> _analysisRepoMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock = new();
    private readonly Mock<ILogger<PositionManagementHandler>> _loggerMock = new();

    private readonly PositionManagementHandler _handler;

    public PositionManagementWorkflowTests()
    {
        _handler = new PositionManagementHandler(
            _positionRepoMock.Object,
            _stopLossManagerMock.Object,
            _breakEvenManagerMock.Object,
            _partialCloseManagerMock.Object,
            _positionCloseManagerMock.Object,
            _analysisRepoMock.Object,
            _unitOfWorkMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task ProcessPositionManagementAsync_WhenPositionFoundAndValid_ShouldUpdatePositionAndReturnSuccess()
    {
        // Arrange
        var message = new TelegramMessage(1001, 100, 500, "یورو ریسک فری شده", DateTime.UtcNow);
        var analysis = new MessageAnalysis(
            message.Id,
            MessageType.TRADE_UPDATE,
            0.95m,
            "{}",
            false,
            DateTime.UtcNow,
            TelegramMessageIntent.PositionManagement,
            action: "MoveStopLossToEntry",
            targetSymbol: "EURUSD",
            processingStatus: "Completed"
        );

        var openPosition = new Position(
            Guid.NewGuid(),
            "EURUSD",
            OrderSide.Buy,
            1.0850m,
            1.0m,
            10
        );

        _positionRepoMock.Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position> { openPosition });

        _stopLossManagerMock.Setup(s => s.UpdateStopLossAsync(openPosition.Id, openPosition.EntryPrice, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        var result = await _handler.ProcessPositionManagementAsync(analysis, message);

        // Assert
        result.Success.Should().BeTrue();
        result.Status.Should().Be("Executed");
        result.ExecutedAction.Should().Be("MoveStopLossToEntry");
        result.AuditReason.Should().Contain("executed action");

        _stopLossManagerMock.Verify(s => s.UpdateStopLossAsync(openPosition.Id, openPosition.EntryPrice, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        _analysisRepoMock.Verify(a => a.Update(It.Is<MessageAnalysis>(m => m.ProcessingStatus == "Executed")), Times.Once);
    }

    [Fact]
    public async Task ProcessPositionManagementAsync_WhenNoActivePositionFound_ShouldReturnIgnoredWithAuditReason()
    {
        // Arrange
        var message = new TelegramMessage(1001, 101, 500, "یورو ریسک فری شده", DateTime.UtcNow);
        var analysis = new MessageAnalysis(
            message.Id,
            MessageType.TRADE_UPDATE,
            0.95m,
            "{}",
            false,
            DateTime.UtcNow,
            TelegramMessageIntent.PositionManagement,
            action: "MoveStopLossToEntry",
            targetSymbol: "EURUSD",
            processingStatus: "Completed"
        );

        _positionRepoMock.Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position>());

        // Act
        var result = await _handler.ProcessPositionManagementAsync(analysis, message);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be("Ignored");
        result.AuditReason.Should().Contain("No matching open position exists");

        _stopLossManagerMock.Verify(s => s.UpdateStopLossAsync(It.IsAny<Guid>(), It.IsAny<decimal?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _analysisRepoMock.Verify(a => a.Update(It.Is<MessageAnalysis>(m => m.ProcessingStatus == "Ignored")), Times.Once);
    }

    [Fact]
    public async Task ProcessPositionManagementAsync_WhenLowConfidenceScore_ShouldReturnFilteredWithAuditReason()
    {
        // Arrange
        var message = new TelegramMessage(1001, 102, 500, "یورو شاید ریسک فری", DateTime.UtcNow);
        var analysis = new MessageAnalysis(
            message.Id,
            MessageType.TRADE_UPDATE,
            0.50m, // Below 0.70 threshold
            "{}",
            false,
            DateTime.UtcNow,
            TelegramMessageIntent.PositionManagement,
            action: "MoveStopLossToEntry",
            targetSymbol: "EURUSD",
            processingStatus: "Completed"
        );

        // Act
        var result = await _handler.ProcessPositionManagementAsync(analysis, message);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be("Filtered");
        result.AuditReason.Should().Contain("below configured minimum threshold");

        _positionRepoMock.Verify(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()), Times.Never);
        _analysisRepoMock.Verify(a => a.Update(It.Is<MessageAnalysis>(m => m.ProcessingStatus == "Filtered")), Times.Once);
    }

    [Fact]
    public async Task ProcessPositionManagementAsync_WhenRiskValidationFails_ShouldReturnFailedWithAuditReason()
    {
        // Arrange
        var message = new TelegramMessage(1001, 103, 500, "یورو ریسک فری شده", DateTime.UtcNow);
        var analysis = new MessageAnalysis(
            message.Id,
            MessageType.TRADE_UPDATE,
            0.95m,
            "{}",
            false,
            DateTime.UtcNow,
            TelegramMessageIntent.PositionManagement,
            action: "MoveStopLossToEntry",
            targetSymbol: "EURUSD",
            processingStatus: "Completed"
        );

        var openPosition = new Position(
            Guid.NewGuid(),
            "EURUSD",
            OrderSide.Buy,
            1.0850m,
            1.0m,
            10
        );

        _positionRepoMock.Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position> { openPosition });

        // Exchange/Risk manager returns false
        _stopLossManagerMock.Setup(s => s.UpdateStopLossAsync(openPosition.Id, openPosition.EntryPrice, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        var result = await _handler.ProcessPositionManagementAsync(analysis, message);

        // Assert
        result.Success.Should().BeFalse();
        result.Status.Should().Be("Failed");
        result.AuditReason.Should().Contain("rejected");

        _analysisRepoMock.Verify(a => a.Update(It.Is<MessageAnalysis>(m => m.ProcessingStatus == "Failed")), Times.Once);
    }
}
