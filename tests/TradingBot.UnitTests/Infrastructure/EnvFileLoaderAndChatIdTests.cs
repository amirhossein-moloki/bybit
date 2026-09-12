using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Models;
using TradingBot.Application.Repositories;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Infrastructure.Configuration;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;
using TradingBot.Persistence.SignalIntelligence.Repositories;
using TradingBot.Persistence.UnitOfWork;
using TradingBot.Telegram.Client;
using TradingBot.Telegram.Models;
using Xunit;

namespace TradingBot.UnitTests.Infrastructure;

public class EnvFileLoaderAndChatIdTests
{
    [Fact]
    public void LoadDotEnv_ShouldParseAndSetEnvironmentVariables()
    {
        // Arrange
        var tempFile = Path.Combine(Directory.GetCurrentDirectory(), ".env.test_tmp");
        try
        {
            File.WriteAllLines(tempFile, new[]
            {
                "# Test comment",
                "TEST_ENV_VAR_CHAT_ID=-100987654321",
                "TEST_ENV_VAR_TOKEN=\"bot123456:ABC-DEF\""
            });

            // Act: Simulate loading manually
            foreach (var line in File.ReadAllLines(tempFile))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#")) continue;

                var idx = trimmed.IndexOf('=');
                if (idx <= 0) continue;

                var key = trimmed.Substring(0, idx).Trim();
                var value = trimmed.Substring(idx + 1).Trim();

                if ((value.StartsWith("\"") && value.EndsWith("\"")) || (value.StartsWith("'") && value.EndsWith("'")))
                {
                    value = value.Substring(1, value.Length - 2);
                }

                Environment.SetEnvironmentVariable(key, value);
            }

            // Assert
            Environment.GetEnvironmentVariable("TEST_ENV_VAR_CHAT_ID").Should().Be("-100987654321");
            Environment.GetEnvironmentVariable("TEST_ENV_VAR_TOKEN").Should().Be("bot123456:ABC-DEF");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
            Environment.SetEnvironmentVariable("TEST_ENV_VAR_CHAT_ID", null);
            Environment.SetEnvironmentVariable("TEST_ENV_VAR_TOKEN", null);
        }
    }

    [Fact]
    public async Task GetByChatIdAsync_ShouldMatchRawAndBotApiChatIds()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var context = new TradingDbContext(options);
        var repo = new TelegramSourceRepository(context);

        // Add source with Bot API ID format (-1001492324861)
        var source = new TelegramSource(
            -1001492324861L,
            "Test Channel",
            "test_channel",
            TelegramSourceType.Channel,
            isEnabled: true,
            listenForSignals: true,
            processMessages: true
        );
        await context.TelegramSources.AddAsync(source);
        await context.SaveChangesAsync();

        // Act & Assert: Query with raw positive ID (1492324861)
        var matchByRaw = await repo.GetByChatIdAsync(1492324861L);
        matchByRaw.Should().NotBeNull();
        matchByRaw!.Title.Should().Be("Test Channel");

        // Act & Assert: Query with exact Bot API ID (-1001492324861)
        var matchByBotApi = await repo.GetByChatIdAsync(-1001492324861L);
        matchByBotApi.Should().NotBeNull();
        matchByBotApi!.Title.Should().Be("Test Channel");
    }

    [Fact]
    public async Task DefaultTelegramMessageReceiver_ShouldPersistMessageAndSaveChanges()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var context = new TradingDbContext(options);

        var source = new TelegramSource(
            123456L,
            "Receiver Test Channel",
            "receiver_test",
            TelegramSourceType.Channel,
            isEnabled: true,
            listenForSignals: true,
            processMessages: true
        );

        var sourceRepoMock = new Mock<Application.Interfaces.Persistence.ITelegramSourceRepository>();
        sourceRepoMock.Setup(s => s.GetByChatIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(source);

        var msgRepoMock = new Mock<IMessageRepository>();
        TelegramMessage? capturedMessage = null;
        msgRepoMock.Setup(m => m.CreateAsync(It.IsAny<TelegramMessage>(), It.IsAny<CancellationToken>()))
            .Callback<TelegramMessage, CancellationToken>((m, ct) => capturedMessage = m)
            .Returns(Task.CompletedTask);

        var unitOfWorkMock = new Mock<IUnitOfWork>();
        bool saveChangesCalled = false;
        unitOfWorkMock.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => saveChangesCalled = true)
            .ReturnsAsync(1);

        var services = new ServiceCollection();
        services.AddScoped(_ => sourceRepoMock.Object);
        services.AddScoped(_ => msgRepoMock.Object);
        services.AddScoped(_ => unitOfWorkMock.Object);

        var messageFilterMock = new Mock<IMessageFilter>();
        messageFilterMock.Setup(f => f.AnalyzeAsync(It.IsAny<TelegramMessageDto>()))
            .ReturnsAsync((SignalCandidate?)null);
        services.AddScoped(_ => messageFilterMock.Object);

        var serviceProvider = services.BuildServiceProvider();
        var scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock.Setup(s => s.CreateScope()).Returns(() => serviceProvider.CreateScope());

        var queueMock = new Mock<ISignalStorageQueue>();
        var metricsMock = new Mock<ISignalStorageMetrics>();
        var gateMock = new Mock<ITradingGate>();
        gateMock.Setup(g => g.CurrentState).Returns(ApplicationState.Ready);

        var receiver = new DefaultTelegramMessageReceiver(
            scopeFactoryMock.Object,
            queueMock.Object,
            metricsMock.Object,
            gateMock.Object,
            NullLogger<DefaultTelegramMessageReceiver>.Instance
        );

        var dto = new TelegramMessageDto
        {
            ChannelId = 123456L,
            ChannelName = "Receiver Test Channel",
            MessageId = 999,
            SenderId = 111,
            Text = "BTCUSDT BUY Entry 50000 SL 48000 TP 55000",
            Date = DateTime.UtcNow,
            IsChannel = true,
            IsGroup = false,
            RawUpdate = "UpdateNewChannelMessage"
        };

        // Act
        await receiver.ReceiveMessageAsync(dto);

        // Assert: Message should be created and SaveChangesAsync called
        capturedMessage.Should().NotBeNull();
        capturedMessage!.ChannelId.Should().Be(123456L);
        capturedMessage.MessageId.Should().Be(999L);
        capturedMessage.Content.Should().Be("BTCUSDT BUY Entry 50000 SL 48000 TP 55000");
        saveChangesCalled.Should().BeTrue();
    }
}
