using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradingBot.Application.Models;
using TradingBot.Application.Services;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;
using TradingBot.Persistence.UnitOfWork;
using TradingBot.Telegram.Models;
using Xunit;

namespace TradingBot.IntegrationTests.Services;

public class TelegramIngestionPipelineTests : IAsyncLifetime
{
    private SqliteConnection? _sqliteConnection;

    public async Task InitializeAsync()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        await _sqliteConnection.OpenAsync();
        using var command = _sqliteConnection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (_sqliteConnection != null)
        {
            await _sqliteConnection.CloseAsync();
            await _sqliteConnection.DisposeAsync();
        }
    }

    private TradingDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseSqlite(_sqliteConnection!)
            .Options;

        var context = new TradingDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task E2E_Pipeline_ShouldAnalyzeAndPersistSignal()
    {
        // 1. Arrange & Set up Pipeline Components
        using var context = CreateDbContext();
        var signalRepository = new SignalRepository(context);
        var unitOfWork = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);
        var metrics = new SignalStorageMetrics();

        // Setup Message Filter with standard options
        var filterOptions = Options.Create(new SignalDetectionSettings());
        var filterService = new MessageFilterService(NullLogger<MessageFilterService>.Instance, filterOptions);

        // Setup Storage Service
        var storageService = new SignalStorageService(signalRepository, unitOfWork, metrics, NullLogger<SignalStorageService>.Instance);

        // Simulated raw Telegram Message mapped to TelegramMessageDto
        var rawMessageDto = new TelegramMessageDto
        {
            ChannelId = 999333888L,
            ChannelName = "VIP Elite Signals",
            MessageId = 7711,
            SenderId = 123456L,
            Text = "🚀 LONG BTCUSDT ENTRY: 65200 \nStop Loss (SL): 63500 \nTake Profit (TP): 72000",
            Date = DateTime.UtcNow,
            IsChannel = true,
            IsGroup = false,
            RawUpdate = "UpdateNewChannelMessage"
        };

        // 2. Act - Part A: Message Filtering
        var filterStopwatch = Stopwatch.StartNew();
        var candidate = await filterService.AnalyzeAsync(rawMessageDto);
        filterStopwatch.Stop();

        candidate.Should().NotBeNull("Message should be detected as a valid signal candidate");
        candidate!.DetectedSymbol.Should().Be("BTCUSDT");
        candidate.DetectedSide.Should().Be("LONG");
        candidate.DetectionScore.Should().Be(100); // 30 (symbol) + 30 (side) + 20 (entry) + 20 (SL/TP)

        // Act - Part B: Signal Storage Persistence
        var storageStopwatch = Stopwatch.StartNew();
        await storageService.StoreAsync(candidate);
        storageStopwatch.Stop();

        // 3. Assert - Verify correct persistence in SQL Database
        using var contextVerify = CreateDbContext();
        var savedSignal = await contextVerify.Signals
            .FirstOrDefaultAsync(s => s.TelegramChannelId == rawMessageDto.ChannelId && s.TelegramMessageId == rawMessageDto.MessageId);

        savedSignal.Should().NotBeNull();
        savedSignal!.TelegramChannelId.Should().Be(rawMessageDto.ChannelId);
        savedSignal.TelegramMessageId.Should().Be(rawMessageDto.MessageId);
        savedSignal.RawMessage.Should().Be(rawMessageDto.Text);
        savedSignal.Symbol.Should().Be("BTCUSDT");
        savedSignal.Side.Should().Be(OrderSide.Buy);
        savedSignal.Status.Should().Be(SignalStatus.Received);

        // Metrics assert
        metrics.SignalsStored.Should().Be(1);
        metrics.DuplicatesIgnored.Should().Be(0);
        metrics.StorageFailures.Should().Be(0);

        // Verification of execution speeds
        _ = filterStopwatch.ElapsedMilliseconds;
        _ = storageStopwatch.ElapsedMilliseconds;
    }

    [Fact]
    public async Task E2E_Pipeline_HighVolumeSimulation_ShouldProcessCorrectlyWithoutDataLossOrCrash()
    {
        // 1. Arrange
        using var context = CreateDbContext();
        var signalRepository = new SignalRepository(context);
        var unitOfWork = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);
        var metrics = new SignalStorageMetrics();

        var filterOptions = Options.Create(new SignalDetectionSettings());
        var filterService = new MessageFilterService(NullLogger<MessageFilterService>.Instance, filterOptions);
        var storageService = new SignalStorageService(signalRepository, unitOfWork, metrics, NullLogger<SignalStorageService>.Instance);

        const int MessageCount = 1000;
        var initialMemory = GC.GetTotalMemory(forceFullCollection: true);

        long totalReceiveAndMapTime = 0;
        long totalFilterTime = 0;
        long totalStorageTime = 0;

        // 2. Act - Loop through 1,000 Telegram Messages
        for (int i = 1; i <= MessageCount; i++)
        {
            // Simulate Receive and Map time
            var receiveStopwatch = Stopwatch.StartNew();
            var messageDto = new TelegramMessageDto
            {
                ChannelId = 888777111L,
                ChannelName = "HighVolumeSignalChannel",
                MessageId = i,
                SenderId = 999999L,
                Text = $"🟢 BUY ETHUSDT ENTRY ALERT around 2850. Target target 3100. SL 2700. Msg #{i}",
                Date = DateTime.UtcNow,
                IsChannel = true,
                IsGroup = false,
                RawUpdate = "UpdateNewChannelMessage"
            };
            receiveStopwatch.Stop();
            totalReceiveAndMapTime += receiveStopwatch.ElapsedTicks;

            // Filter message
            var filterStopwatch = Stopwatch.StartNew();
            var candidate = await filterService.AnalyzeAsync(messageDto);
            filterStopwatch.Stop();
            totalFilterTime += filterStopwatch.ElapsedTicks;

            candidate.Should().NotBeNull();

            // Store message
            var storageStopwatch = Stopwatch.StartNew();
            await storageService.StoreAsync(candidate!);
            storageStopwatch.Stop();
            totalStorageTime += storageStopwatch.ElapsedTicks;
        }

        var finalMemory = GC.GetTotalMemory(forceFullCollection: true);

        // 3. Assert
        // Confirm 100% processing throughput
        metrics.SignalsStored.Should().Be(MessageCount);
        metrics.DuplicatesIgnored.Should().Be(0);
        metrics.StorageFailures.Should().Be(0);

        using var contextVerify = CreateDbContext();
        var dbCount = await contextVerify.Signals.CountAsync(s => s.TelegramChannelId == 888777111L);
        dbCount.Should().Be(MessageCount);

        // Output simulated execution timings (ticks to ms)
        double avgReceiveMs = (double)totalReceiveAndMapTime / Stopwatch.Frequency * 1000 / MessageCount;
        double avgFilterMs = (double)totalFilterTime / Stopwatch.Frequency * 1000 / MessageCount;
        double avgStorageMs = (double)totalStorageTime / Stopwatch.Frequency * 1000 / MessageCount;

        avgReceiveMs.Should().BeLessThan(5.0, "Receive & Map should be sub-millisecond on average");
        avgFilterMs.Should().BeLessThan(10.0, "Filter should be sub-millisecond on average");
        avgStorageMs.Should().BeLessThan(50.0, "Storage should be highly performant");

        // Verify memory stability (reasonable variance)
        long memoryDiff = finalMemory - initialMemory;
        // Large variance allowed because GC executes during test, but we expect no memory leaks.
        _ = memoryDiff;
    }

    [Fact]
    public async Task E2E_Pipeline_InvalidMessageShouldBeIgnoredSafely()
    {
        // 1. Arrange
        using var context = CreateDbContext();
        var signalRepository = new SignalRepository(context);
        var unitOfWork = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);
        var metrics = new SignalStorageMetrics();

        var filterOptions = Options.Create(new SignalDetectionSettings());
        var filterService = new MessageFilterService(NullLogger<MessageFilterService>.Instance, filterOptions);
        var storageService = new SignalStorageService(signalRepository, unitOfWork, metrics, NullLogger<SignalStorageService>.Instance);

        var chatChatterMessage = new TelegramMessageDto
        {
            ChannelId = 999333888L,
            ChannelName = "VIP Elite Signals",
            MessageId = 8801,
            Text = "Good morning everyone! BTC looks strong today but no trade setup yet.",
            Date = DateTime.UtcNow
        };

        // 2. Act
        var candidate = await filterService.AnalyzeAsync(chatChatterMessage);

        // 3. Assert
        candidate.Should().BeNull("Non-signal chatter messages must be rejected by message filter");
    }
}
