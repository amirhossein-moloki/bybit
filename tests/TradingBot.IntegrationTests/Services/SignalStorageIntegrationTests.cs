using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Models;
using TradingBot.Application.Services;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;
using TradingBot.Persistence.UnitOfWork;
using Xunit;

namespace TradingBot.IntegrationTests.Services;

public class SignalStorageIntegrationTests : IAsyncLifetime
{
    private SqliteConnection? _sqliteConnection;

    public async Task InitializeAsync()
    {
        // Use Sqlite memory database for super-fast, reliable isolation inside container sandbox
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
    public async Task E2E_SignalCandidateStorage_ShouldSaveAndPreventDuplicates()
    {
        // Arrange
        using var context = CreateDbContext();
        var signalRepository = new SignalRepository(context);
        var unitOfWork = new UnitOfWork(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<UnitOfWork>.Instance);
        var metrics = new SignalStorageMetrics();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<SignalStorageService>.Instance;

        var service = new SignalStorageService(signalRepository, unitOfWork, metrics, logger);

        var candidate = new SignalCandidate
        {
            ChannelId = 999111222L,
            MessageId = 4455,
            RawText = "🚀 ENTRY ALERT: BUY BTCUSDT around 60500",
            DetectedSymbol = "BTCUSDT",
            DetectedSide = "LONG",
            DetectionScore = 90,
            DetectedAt = DateTime.UtcNow
        };

        // Act - First Store (Succeeds)
        await service.StoreAsync(candidate);

        // Assert - Verify database save
        using var context2 = CreateDbContext();
        var savedSignal = await context2.Signals.FirstOrDefaultAsync(s => s.TelegramChannelId == candidate.ChannelId && s.TelegramMessageId == candidate.MessageId);

        savedSignal.Should().NotBeNull();
        savedSignal!.TelegramChannelId.Should().Be(candidate.ChannelId);
        savedSignal.TelegramMessageId.Should().Be(candidate.MessageId);
        savedSignal.Symbol.Should().Be("BTCUSDT");
        savedSignal.Side.Should().Be(OrderSide.Buy);
        savedSignal.RawMessage.Should().Be(candidate.RawText);
        savedSignal.Source.Should().Be(candidate.ChannelId.ToString());
        savedSignal.Status.Should().Be(SignalStatus.Received);

        metrics.SignalsStored.Should().Be(1);
        metrics.DuplicatesIgnored.Should().Be(0);
        metrics.StorageFailures.Should().Be(0);

        // Act - Second Store with exact same Channel/Message ID (Should be ignored safely)
        var duplicateCandidate = new SignalCandidate
        {
            ChannelId = 999111222L,
            MessageId = 4455,
            RawText = "🚀 ENTRY ALERT: BUY BTCUSDT around 60500 (REPUBLISHED)",
            DetectedSymbol = "BTCUSDT",
            DetectedSide = "LONG",
            DetectionScore = 90,
            DetectedAt = DateTime.UtcNow
        };

        await service.StoreAsync(duplicateCandidate);

        // Assert - Verify that NO duplicate signal was created, and duplicates metric went up
        using var context3 = CreateDbContext();
        var count = await context3.Signals.CountAsync(s => s.TelegramChannelId == candidate.ChannelId && s.TelegramMessageId == candidate.MessageId);
        count.Should().Be(1); // Still exactly one record

        metrics.SignalsStored.Should().Be(1);
        metrics.DuplicatesIgnored.Should().Be(1);
        metrics.StorageFailures.Should().Be(0);
    }
}
