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
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.UnitTests.Telegram;

public class TelegramSourceServiceTests
{
    private readonly Mock<ITelegramSourceRepository> _mockRepo;
    private readonly Mock<ITelegramDiscoveryClient> _mockDiscovery;
    private readonly TelegramSourceService _service;

    public TelegramSourceServiceTests()
    {
        _mockRepo = new Mock<ITelegramSourceRepository>();
        _mockDiscovery = new Mock<ITelegramDiscoveryClient>();
        _service = new TelegramSourceService(
            _mockRepo.Object,
            NullLogger<TelegramSourceService>.Instance,
            _mockDiscovery.Object
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
}
