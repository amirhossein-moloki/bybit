using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Models;
using TradingBot.Application.Repositories;
using TradingBot.Application.Services;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.UnitTests.Services;

public class SignalStorageServiceTests
{
    private readonly Mock<ISignalRepository> _signalRepositoryMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly SignalStorageMetrics _metrics;
    private readonly Mock<ILogger<SignalStorageService>> _loggerMock;
    private readonly SignalStorageService _service;

    public SignalStorageServiceTests()
    {
        _signalRepositoryMock = new Mock<ISignalRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _metrics = new SignalStorageMetrics();
        _loggerMock = new Mock<ILogger<SignalStorageService>>();

        _service = new SignalStorageService(
            _signalRepositoryMock.Object,
            _unitOfWorkMock.Object,
            _metrics,
            _loggerMock.Object
        );
    }

    [Fact]
    public async Task StoreAsync_WithValidCandidate_ShouldMapAndSaveSuccessfully()
    {
        // Arrange
        var candidate = new SignalCandidate
        {
            ChannelId = 987654321,
            MessageId = 1234,
            RawText = "🚀 BTC LONG at 60000",
            DetectedSymbol = "BTCUSDT",
            DetectedSide = "LONG",
            DetectionScore = 100,
            DetectedAt = DateTime.UtcNow
        };

        _signalRepositoryMock.Setup(r => r.ExistsAsync(candidate.ChannelId, candidate.MessageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        Signal? savedSignal = null;
        _signalRepositoryMock.Setup(r => r.SaveAsync(It.IsAny<Signal>(), It.IsAny<CancellationToken>()))
            .Callback<Signal, CancellationToken>((s, ct) => savedSignal = s)
            .Returns(Task.CompletedTask);

        // Act
        await _service.StoreAsync(candidate);

        // Assert
        _signalRepositoryMock.Verify(r => r.ExistsAsync(candidate.ChannelId, candidate.MessageId, It.IsAny<CancellationToken>()), Times.Once);
        _signalRepositoryMock.Verify(r => r.SaveAsync(It.IsAny<Signal>(), It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);

        savedSignal.Should().NotBeNull();
        savedSignal!.TelegramChannelId.Should().Be(candidate.ChannelId);
        savedSignal.TelegramMessageId.Should().Be(candidate.MessageId);
        savedSignal.Source.Should().Be(candidate.ChannelId.ToString());
        savedSignal.RawMessage.Should().Be(candidate.RawText);
        savedSignal.Symbol.Should().Be(candidate.DetectedSymbol);
        savedSignal.Side.Should().Be(OrderSide.Buy);
        savedSignal.Status.Should().Be(SignalStatus.Received);
        savedSignal.CreatedAt.Should().Be(candidate.DetectedAt);

        _metrics.SignalsStored.Should().Be(1);
        _metrics.DuplicatesIgnored.Should().Be(0);
        _metrics.StorageFailures.Should().Be(0);
    }

    [Fact]
    public async Task StoreAsync_WithDuplicateCandidate_ShouldIgnoreAndIncrementDuplicateMetrics()
    {
        // Arrange
        var candidate = new SignalCandidate
        {
            ChannelId = 987654321,
            MessageId = 1234,
            RawText = "🚀 BTC LONG at 60000",
            DetectedSymbol = "BTCUSDT",
            DetectedSide = "LONG",
            DetectionScore = 100,
            DetectedAt = DateTime.UtcNow
        };

        _signalRepositoryMock.Setup(r => r.ExistsAsync(candidate.ChannelId, candidate.MessageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await _service.StoreAsync(candidate);

        // Assert
        _signalRepositoryMock.Verify(r => r.ExistsAsync(candidate.ChannelId, candidate.MessageId, It.IsAny<CancellationToken>()), Times.Once);
        _signalRepositoryMock.Verify(r => r.SaveAsync(It.IsAny<Signal>(), It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);

        _metrics.SignalsStored.Should().Be(0);
        _metrics.DuplicatesIgnored.Should().Be(1);
        _metrics.StorageFailures.Should().Be(0);
    }

    [Fact]
    public async Task StoreAsync_WithDatabaseFailure_ShouldRollbackAndThrowException()
    {
        // Arrange
        var candidate = new SignalCandidate
        {
            ChannelId = 987654321,
            MessageId = 1234,
            RawText = "🚀 BTC LONG at 60000",
            DetectedSymbol = "BTCUSDT",
            DetectedSide = "LONG",
            DetectionScore = 100,
            DetectedAt = DateTime.UtcNow
        };

        _signalRepositoryMock.Setup(r => r.ExistsAsync(candidate.ChannelId, candidate.MessageId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _signalRepositoryMock.Setup(r => r.SaveAsync(It.IsAny<Signal>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Database connection lost"));

        // Act
        Func<Task> act = async () => await _service.StoreAsync(candidate);

        // Assert
        await act.Should().ThrowAsync<Exception>().WithMessage("Database connection lost");

        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);

        _metrics.SignalsStored.Should().Be(0);
        _metrics.DuplicatesIgnored.Should().Be(0);
        _metrics.StorageFailures.Should().Be(1);
    }

    [Fact]
    public async Task StoreAsync_WithInvalidCandidate_ShouldThrowExceptionAndNotSave()
    {
        // Arrange
        var candidate = new SignalCandidate
        {
            ChannelId = 987654321,
            MessageId = 1234,
            RawText = "🚀 BTC LONG at 60000",
            DetectedSymbol = "", // Invalid symbol
            DetectedSide = "LONG",
            DetectionScore = 100,
            DetectedAt = DateTime.UtcNow
        };

        // Act
        Func<Task> act = async () => await _service.StoreAsync(candidate);

        // Assert
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*detected symbol*");

        _signalRepositoryMock.Verify(r => r.ExistsAsync(It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
        _signalRepositoryMock.Verify(r => r.SaveAsync(It.IsAny<Signal>(), It.IsAny<CancellationToken>()), Times.Never);

        _metrics.SignalsStored.Should().Be(0);
        _metrics.DuplicatesIgnored.Should().Be(0);
        _metrics.StorageFailures.Should().Be(1);
    }
}
