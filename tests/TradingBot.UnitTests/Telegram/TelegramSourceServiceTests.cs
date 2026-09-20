using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Interfaces.Persistence;
using TradingBot.Application.Models;
using TradingBot.Application.Services;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Domain.SignalIntelligence.Enums;
using Xunit;

namespace TradingBot.UnitTests.Telegram;

public class TelegramSourceServiceTests
{
    private readonly Mock<ITelegramSourceRepository> _mockRepo;
    private readonly Mock<ITelegramDiscoveryClient> _mockDiscovery;
    private readonly Mock<IMessageRepository> _mockMessageRepo;
    private readonly TelegramSourceService _service;

    public TelegramSourceServiceTests()
    {
        _mockRepo = new Mock<ITelegramSourceRepository>();
        _mockDiscovery = new Mock<ITelegramDiscoveryClient>();
        _mockMessageRepo = new Mock<IMessageRepository>();

        _service = new TelegramSourceService(
            _mockRepo.Object,
            NullLogger<TelegramSourceService>.Instance,
            _mockDiscovery.Object,
            _mockMessageRepo.Object
        );
    }

    [Fact]
    public async Task CreateSourceAsync_ShouldCreateNewSource_WhenNotExists()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetByChatIdAsync(1001, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TelegramSource?)null);

        var dto = new CreateTelegramSourceDto(
            1001,
            "VIP Crypto Channel",
            "@vip_crypto",
            "Channel",
            IsEnabled: true,
            ListenForSignals: true,
            ProcessMessages: true
        );

        // Act
        var result = await _service.CreateSourceAsync(dto);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1001, result.TelegramChatId);
        Assert.Equal("VIP Crypto Channel", result.Title);
        Assert.Equal("@vip_crypto", result.Username);
        Assert.True(result.IsEnabled);
        Assert.True(result.ListenForSignals);
        _mockRepo.Verify(r => r.AddAsync(It.IsAny<TelegramSource>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateSourceAsync_ShouldThrow_WhenChatIdAlreadyExists()
    {
        // Arrange
        var existing = new TelegramSource(1001, "Existing Channel");
        _mockRepo.Setup(r => r.GetByChatIdAsync(1001, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var dto = new CreateTelegramSourceDto(1001, "Duplicate Channel");

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateSourceAsync(dto));
    }

    [Fact]
    public async Task SyncSourcesAsync_ShouldBeIdempotent_AndPreserveCapabilities()
    {
        // Arrange
        var discoveredChats = new List<DiscoveredTelegramChatDto>
        {
            new DiscoveredTelegramChatDto(1001, "Updated Channel Title", "updated_user", true, false),
            new DiscoveredTelegramChatDto(2002, "New Group Title", "new_group", false, true)
        };

        _mockDiscovery.Setup(d => d.GetDialogsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(discoveredChats);

        // Existing source with custom disabled capability
        var existingSource = new TelegramSource(1001, "Old Title", "@old_user", TelegramSourceType.Channel, isEnabled: false, listenForSignals: false);

        _mockRepo.Setup(r => r.GetByChatIdAsync(1001, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingSource);
        _mockRepo.Setup(r => r.GetByChatIdAsync(2002, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TelegramSource?)null);

        // Act
        var result = await _service.SyncSourcesAsync();

        // Assert
        Assert.Equal(2, result.DiscoveredCount);
        Assert.Equal(1, result.NewCount);
        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(0, result.ErrorCount);

        // Verify capabilities were preserved for existing source
        Assert.False(existingSource.IsEnabled);
        Assert.False(existingSource.ListenForSignals);
        Assert.Equal("Updated Channel Title", existingSource.Title);
    }

    [Fact]
    public async Task UpdateSourceAsync_ShouldUpdateCapabilitiesAndPause()
    {
        // Arrange
        var sourceId = Guid.NewGuid();
        var source = new TelegramSource(1001, "Test Channel");
        _mockRepo.Setup(r => r.GetByIdAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(source);

        var updateDto = new UpdateTelegramSourceDto(
            IsEnabled: true,
            ListenForSignals: false,
            ProcessMessages: true,
            PauseMinutes: 60
        );

        // Act
        var result = await _service.UpdateSourceAsync(sourceId, updateDto);

        // Assert
        Assert.True(result.IsEnabled);
        Assert.False(result.ListenForSignals);
        Assert.True(result.ProcessMessages);
        Assert.Equal("Paused", result.Status);
    }

    [Fact]
    public async Task GetSourceMessagesAsync_ShouldThrowKeyNotFoundException_WhenSourceDoesNotExist()
    {
        // Arrange
        var invalidId = Guid.NewGuid();
        _mockRepo.Setup(r => r.GetByIdAsync(invalidId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TelegramSource?)null);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.GetSourceMessagesAsync(invalidId));
    }

    [Fact]
    public async Task GetSourceMessagesAsync_ShouldReturnEmptyList_WhenTelegramMessagesTableIsEmpty()
    {
        // Arrange
        var sourceId = Guid.NewGuid();
        var source = new TelegramSource(1001, "Test Channel");
        _mockRepo.Setup(r => r.GetByIdAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(source);

        _mockMessageRepo.Setup(m => m.GetRecentMessagesForChannelAsync(1001, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TelegramMessage>());

        // Act
        var result = await _service.GetSourceMessagesAsync(sourceId, page: 1, pageSize: 20);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetSourceMessagesAsync_ShouldPaginateCorrectly_AndHandleNullContentSafely()
    {
        // Arrange
        var sourceId = Guid.NewGuid();
        var source = new TelegramSource(1001, "Test Channel");
        _mockRepo.Setup(r => r.GetByIdAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(source);

        var messages = new List<TelegramMessage>
        {
            new TelegramMessage(1001, 1, 10, "Message 1", DateTime.UtcNow.AddMinutes(-5)),
            new TelegramMessage(1001, 2, 10, "Message 2", DateTime.UtcNow.AddMinutes(-4)),
            new TelegramMessage(1001, 3, 10, "Message 3", DateTime.UtcNow.AddMinutes(-3))
        };

        _mockMessageRepo.Setup(m => m.GetRecentMessagesForChannelAsync(1001, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(messages);

        // Act
        var page1 = await _service.GetSourceMessagesAsync(sourceId, page: 1, pageSize: 2);

        // Assert
        Assert.Equal(2, page1.Count);
        Assert.Equal(1, page1[0].MessageId);
        Assert.Equal(2, page1[1].MessageId);
    }

    [Fact]
    public async Task GetLiveMessagePipelineAsync_ShouldThrowKeyNotFoundException_WhenSourceIdIsProvidedButNotFound()
    {
        // Arrange
        var invalidSourceId = Guid.NewGuid();
        _mockRepo.Setup(r => r.GetByIdAsync(invalidSourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((TelegramSource?)null);

        // Act & Assert
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.GetLiveMessagePipelineAsync(invalidSourceId));
    }

    [Fact]
    public async Task GetLiveMessagePipelineAsync_ShouldReturnEmptyList_WhenNoMessagesExist()
    {
        // Arrange
        _mockRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TelegramSource>());

        // Act
        var result = await _service.GetLiveMessagePipelineAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetLiveMessagePipelineAsync_WithPositionManagementAnalysis_ShouldReturnFormattedLifecycleTrace()
    {
        // Arrange
        var source = new TelegramSource(1001, "Forex Signals Channel");
        _mockRepo.Setup(r => r.GetByIdAsync(source.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(source);
        _mockRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TelegramSource> { source });

        var message = new TelegramMessage(1001, 48269, 500, "یورو ریسک فری شده", DateTime.UtcNow);
        _mockMessageRepo.Setup(m => m.GetRecentMessagesForChannelAsync(1001, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TelegramMessage> { message });

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
            processingStatus: "Executed",
            extractedMetadata: "{}"
        );

        var mockAnalysisRepo = new Mock<IMessageAnalysisRepository>();
        mockAnalysisRepo.Setup(a => a.GetByMessageIdAsync(message.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(analysis);

        var mockServiceProvider = new Mock<IServiceProvider>();
        mockServiceProvider.Setup(sp => sp.GetService(typeof(IMessageAnalysisRepository)))
            .Returns(mockAnalysisRepo.Object);

        var mockScope = new Mock<Microsoft.Extensions.DependencyInjection.IServiceScope>();
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);

        var mockScopeFactory = new Mock<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>();
        mockScopeFactory.Setup(sf => sf.CreateScope()).Returns(mockScope.Object);

        mockServiceProvider.Setup(sp => sp.GetService(typeof(Microsoft.Extensions.DependencyInjection.IServiceScopeFactory)))
            .Returns(mockScopeFactory.Object);

        var service = new TelegramSourceService(
            _mockRepo.Object,
            NullLogger<TelegramSourceService>.Instance,
            _mockDiscovery.Object,
            _mockMessageRepo.Object,
            null,
            mockServiceProvider.Object
        );

        // Act
        var result = await service.GetLiveMessagePipelineAsync(source.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);

        var item = result.First();
        Assert.NotNull(item.Signal);
        Assert.Equal("Position Management Detected", item.Signal.Status);
        Assert.Equal("EURUSD", item.Signal.Symbol);
        Assert.Equal("MoveStopLossToEntry", item.Signal.Side);

        Assert.NotNull(item.RiskDecision);
        Assert.Equal("Validated Existing Position", item.RiskDecision.Decision);

        Assert.NotNull(item.Execution);
        Assert.Equal("Stop Loss Updated Successfully", item.Execution.OrderStatus);
    }

    [Fact]
    public async Task GetPipelineDiagnosticsAsync_ShouldReturnCorrectCountsAndTimestamps()
    {
        // Arrange
        var sources = new List<TelegramSource> { new TelegramSource(1001, "Source 1") };
        _mockRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(sources);

        var lastReceived = new TelegramMessage(1001, 10, 1, "Latest", DateTime.UtcNow);
        var lastProcessed = new TelegramMessage(1001, 9, 1, "Processed", DateTime.UtcNow.AddMinutes(-1));
        lastProcessed.MarkProcessed();

        _mockMessageRepo.Setup(m => m.GetTotalCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(15);
        _mockMessageRepo.Setup(m => m.GetLastReceivedMessageAsync(It.IsAny<CancellationToken>())).ReturnsAsync(lastReceived);
        _mockMessageRepo.Setup(m => m.GetLastProcessedMessageAsync(It.IsAny<CancellationToken>())).ReturnsAsync(lastProcessed);

        // Act
        var diagnostics = await _service.GetPipelineDiagnosticsAsync();

        // Assert
        Assert.NotNull(diagnostics);
        Assert.Equal(1, diagnostics.RegisteredSourcesCount);
        Assert.Equal(15, diagnostics.TotalReceivedMessagesCount);
        Assert.Equal(lastReceived.ReceivedAt, diagnostics.LastReceivedMessageAt);
        Assert.Equal(lastProcessed.ReceivedAt, diagnostics.LastProcessedMessageAt);
    }
}
