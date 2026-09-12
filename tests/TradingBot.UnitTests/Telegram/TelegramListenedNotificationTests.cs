using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Interfaces.Persistence;
using TradingBot.Application.Monitoring.Configuration;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Telegram.Client;
using TradingBot.Telegram.Configuration;
using TradingBot.Telegram.Interfaces;
using TradingBot.Telegram.Models;
using Xunit;

namespace TradingBot.UnitTests.Telegram;

public class TelegramListenedNotificationTests
{
    [Fact]
    public async Task DefaultTelegramMessageReceiver_ShouldForwardListenedMessageAsNotification()
    {
        // Arrange
        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();

        mockScopeFactory.Setup(s => s.CreateScope()).Returns(mockScope.Object);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);

        var mockSourceRepo = new Mock<ITelegramSourceRepository>();
        var mockNotifRepo = new Mock<INotificationRepository>();
        var mockUnitOfWork = new Mock<TradingBot.Application.Repositories.IUnitOfWork>();
        var mockQueue = new Mock<ISignalStorageQueue>();
        var mockMetrics = new Mock<ISignalStorageMetrics>();
        var mockTradingGate = new Mock<ITradingGate>();
        var mockFilter = new Mock<IMessageFilter>();
        var mockLogger = new Mock<ILogger<DefaultTelegramMessageReceiver>>();

        var telegramSource = new TelegramSource(
            telegramChatId: 1001234567,
            title: "Test Monitored Signal Group",
            isEnabled: true,
            listenForSignals: true,
            processMessages: true
        );

        mockSourceRepo.Setup(r => r.GetByChatIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(telegramSource);

        var notificationOptions = new NotificationOptions
        {
            Enabled = true,
            Telegram = new TelegramNotificationSettings
            {
                Enabled = true,
                ChatId = "987654321"
            }
        };

        var optionsWrapper = Options.Create(notificationOptions);

        mockServiceProvider.Setup(sp => sp.GetService(typeof(ITelegramSourceRepository)))
            .Returns(mockSourceRepo.Object);
        mockServiceProvider.Setup(sp => sp.GetService(typeof(INotificationRepository)))
            .Returns(mockNotifRepo.Object);
        mockServiceProvider.Setup(sp => sp.GetService(typeof(TradingBot.Application.Repositories.IUnitOfWork)))
            .Returns(mockUnitOfWork.Object);
        mockServiceProvider.Setup(sp => sp.GetService(typeof(IOptions<NotificationOptions>)))
            .Returns(optionsWrapper);
        mockServiceProvider.Setup(sp => sp.GetService(typeof(IMessageFilter)))
            .Returns(mockFilter.Object);

        var receiver = new DefaultTelegramMessageReceiver(
            mockScopeFactory.Object,
            mockQueue.Object,
            mockMetrics.Object,
            mockTradingGate.Object,
            mockLogger.Object
        );

        var testMessage = new TelegramMessageDto
        {
            ChannelId = 1001234567,
            ChannelName = "Test Monitored Signal Group",
            MessageId = 42,
            SenderId = 12345,
            Text = "BUY BTCUSDT Entry: 65000 TP: 68000 SL: 63000",
            Date = DateTime.UtcNow
        };

        // Act
        await receiver.ReceiveMessageAsync(testMessage);

        // Assert
        mockNotifRepo.Verify(r => r.AddAsync(
            It.Is<Notification>(n =>
                n.EventType == "TelegramListenedMessage" &&
                n.Recipient == "987654321" &&
                n.Message.Contains("BUY BTCUSDT") &&
                n.Message.Contains("Test Monitored Signal Group")),
            It.IsAny<CancellationToken>()
        ), Times.Once);

        mockUnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task TelegramMonitoredSourcesReporter_ShouldCreateSummaryNotificationEveryInterval()
    {
        // Arrange
        var mockScopeFactory = new Mock<IServiceScopeFactory>();
        var mockScope = new Mock<IServiceScope>();
        var mockServiceProvider = new Mock<IServiceProvider>();

        mockScopeFactory.Setup(s => s.CreateScope()).Returns(mockScope.Object);
        mockScope.Setup(s => s.ServiceProvider).Returns(mockServiceProvider.Object);

        var mockSourceRepo = new Mock<ITelegramSourceRepository>();
        var mockNotifRepo = new Mock<INotificationRepository>();
        var mockUnitOfWork = new Mock<TradingBot.Application.Repositories.IUnitOfWork>();
        var mockTelegramClient = new Mock<ITelegramClient>();
        var mockLogger = new Mock<ILogger<TelegramMonitoredSourcesReporter>>();

        var sources = new List<TelegramSource>
        {
            new TelegramSource(100111, "Alpha Signals Group", isEnabled: true, listenForSignals: true, processMessages: true),
            new TelegramSource(100222, "Beta Crypto Channel", isEnabled: true, listenForSignals: true, processMessages: false)
        };

        mockSourceRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(sources);

        var telegramOptions = Options.Create(new TelegramOptions { Enabled = true });
        var notificationOptions = Options.Create(new NotificationOptions
        {
            Enabled = true,
            Telegram = new TelegramNotificationSettings
            {
                Enabled = true,
                ChatId = "987654321"
            }
        });

        mockServiceProvider.Setup(sp => sp.GetService(typeof(ITelegramSourceRepository)))
            .Returns(mockSourceRepo.Object);
        mockServiceProvider.Setup(sp => sp.GetService(typeof(INotificationRepository)))
            .Returns(mockNotifRepo.Object);
        mockServiceProvider.Setup(sp => sp.GetService(typeof(TradingBot.Application.Repositories.IUnitOfWork)))
            .Returns(mockUnitOfWork.Object);

        var reporter = new TelegramMonitoredSourcesReporter(
            mockScopeFactory.Object,
            mockTelegramClient.Object,
            telegramOptions,
            notificationOptions,
            mockLogger.Object,
            TimeSpan.FromMinutes(5)
        );

        // Act
        await reporter.SendMonitoredSourcesReportAsync(CancellationToken.None);

        // Assert
        mockNotifRepo.Verify(r => r.AddAsync(
            It.Is<Notification>(n =>
                n.EventType == "TelegramMonitoredSourcesReport" &&
                n.Recipient == "987654321" &&
                n.Message.Contains("Alpha Signals Group") &&
                n.Message.Contains("Beta Crypto Channel")),
            It.IsAny<CancellationToken>()
        ), Times.Once);

        mockUnitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
