using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Domain.SignalIntelligence.Enums;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.SignalIntelligence.Repositories;
using Xunit;

namespace TradingBot.IntegrationTests.SignalIntelligence;

public class SignalIntelligenceRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer? _postgresContainer;
    private SqliteConnection? _sqliteConnection;
    private bool _useSqlite = false;

    public SignalIntelligenceRepositoryTests()
    {
        try
        {
            _postgresContainer = new PostgreSqlBuilder()
                .WithImage("postgres:15-alpine")
                .Build();
        }
        catch
        {
            _useSqlite = true;
        }
    }

    public async Task InitializeAsync()
    {
        if (!_useSqlite && _postgresContainer != null)
        {
            try
            {
                await _postgresContainer.StartAsync();
            }
            catch
            {
                _useSqlite = true;
            }
        }

        if (_useSqlite)
        {
            _sqliteConnection = new SqliteConnection("DataSource=:memory:");
            await _sqliteConnection.OpenAsync();
            using var command = _sqliteConnection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys = ON;";
            await command.ExecuteNonQueryAsync();
        }
    }

    public async Task DisposeAsync()
    {
        if (_postgresContainer != null)
        {
            try
            {
                await _postgresContainer.DisposeAsync();
            }
            catch
            {
                // Ignore
            }
        }

        if (_sqliteConnection != null)
        {
            try
            {
                await _sqliteConnection.CloseAsync();
                await _sqliteConnection.DisposeAsync();
            }
            catch
            {
                // Ignore
            }
        }
    }

    private TradingDbContext CreateDbContext()
    {
        DbContextOptions<TradingDbContext> options;

        if (_useSqlite)
        {
            options = new DbContextOptionsBuilder<TradingDbContext>()
                .UseSqlite(_sqliteConnection!)
                .Options;
        }
        else
        {
            options = new DbContextOptionsBuilder<TradingDbContext>()
                .UseNpgsql(_postgresContainer!.GetConnectionString())
                .Options;
        }

        var context = new TradingDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    [Fact]
    public async Task PersistAndRetrieveTelegramMessage_ShouldSucceed_WhenValidMessageSaved()
    {
        // Arrange
        using var context = CreateDbContext();
        var repository = new MessageRepository(context);

        var message = new TelegramMessage(
            channelId: 111111111,
            messageId: 42,
            senderId: 222222222,
            content: "Signal Message content",
            receivedAt: DateTime.UtcNow
        );

        // Act - Save
        await repository.CreateAsync(message, CancellationToken.None);
        await context.SaveChangesAsync();

        // Act - Retrieve
        using var context2 = CreateDbContext();
        var repository2 = new MessageRepository(context2);
        var retrieved = await repository2.GetByIdAsync(message.Id, CancellationToken.None);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(message.Id);
        retrieved.ChannelId.Should().Be(111111111);
        retrieved.MessageId.Should().Be(42);
        retrieved.SenderId.Should().Be(222222222);
        retrieved.Content.Should().Be("Signal Message content");
        retrieved.Processed.Should().BeFalse();
    }

    [Fact]
    public async Task GetByChannelMessageIdAsync_ShouldReturnCorrectMessage()
    {
        // Arrange
        using var context = CreateDbContext();
        var repository = new MessageRepository(context);

        var message = new TelegramMessage(111111111, 42, 222222222, "Content", DateTime.UtcNow);
        await repository.CreateAsync(message, CancellationToken.None);
        await context.SaveChangesAsync();

        // Act
        var retrieved = await repository.GetByChannelMessageIdAsync(111111111, 42, CancellationToken.None);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(message.Id);
    }

    [Fact]
    public async Task MarkProcessedAsync_ShouldUpdateStateToProcessed()
    {
        // Arrange
        using var context = CreateDbContext();
        var repository = new MessageRepository(context);

        var message = new TelegramMessage(111111111, 42, 222222222, "Content", DateTime.UtcNow);
        await repository.CreateAsync(message, CancellationToken.None);
        await context.SaveChangesAsync();

        // Act
        await repository.MarkProcessedAsync(message.Id, CancellationToken.None);
        await context.SaveChangesAsync();

        // Assert
        using var context2 = CreateDbContext();
        var repository2 = new MessageRepository(context2);
        var retrieved = await repository2.GetByIdAsync(message.Id, CancellationToken.None);
        retrieved.Should().NotBeNull();
        retrieved!.Processed.Should().BeTrue();
    }

    [Fact]
    public async Task UniqueConstraint_ShouldThrowDbUpdateException_WhenInsertingDuplicateChannelAndMessageId()
    {
        // Arrange
        using var context = CreateDbContext();
        var repository = new MessageRepository(context);

        var msg1 = new TelegramMessage(999, 100, null, "First Message", DateTime.UtcNow);
        var msg2 = new TelegramMessage(999, 100, null, "Duplicate Message", DateTime.UtcNow);

        await repository.CreateAsync(msg1, CancellationToken.None);
        await context.SaveChangesAsync();

        // Act & Assert
        await repository.CreateAsync(msg2, CancellationToken.None);

        Func<Task> act = async () => await context.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task PersistAndRetrieveMessageAnalysis_ShouldSucceed_WhenValidAnalysisSaved()
    {
        // Arrange
        using var context = CreateDbContext();
        var messageRepo = new MessageRepository(context);
        var analysisRepo = new MessageAnalysisRepository(context);

        var message = new TelegramMessage(111111111, 42, 222222222, "Content", DateTime.UtcNow);
        await messageRepo.CreateAsync(message, CancellationToken.None);
        await context.SaveChangesAsync();

        var analysis = new MessageAnalysis(
            telegramMessageId: message.Id,
            messageType: MessageType.SIGNAL,
            confidence: 0.95m,
            extractedData: "{\"symbol\":\"BTCUSDT\"}",
            aiUsed: true,
            processedAt: DateTime.UtcNow
        );

        // Act - Save
        await analysisRepo.CreateAsync(analysis, CancellationToken.None);
        await context.SaveChangesAsync();

        // Act - Retrieve
        using var context2 = CreateDbContext();
        var analysisRepo2 = new MessageAnalysisRepository(context2);
        var retrieved = await analysisRepo2.GetByMessageIdAsync(message.Id, CancellationToken.None);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(analysis.Id);
        retrieved.TelegramMessageId.Should().Be(message.Id);
        retrieved.MessageType.Should().Be(MessageType.SIGNAL);
        retrieved.Confidence.Should().Be(0.95m);
        retrieved.ExtractedData.Should().Be("{\"symbol\":\"BTCUSDT\"}");
        retrieved.AIUsed.Should().BeTrue();
    }

    [Fact]
    public async Task PersistAndRetrieveSignalContext_ShouldSucceed_WhenValidContextSaved()
    {
        // Arrange
        using var context = CreateDbContext();
        var signal = new Signal("TELEGRAM", "BUY BTCUSDT @ 45000", "BTCUSDT", OrderSide.Buy, 45000m, 1m);
        context.Signals.Add(signal);
        await context.SaveChangesAsync();

        var contextRepo = new SignalContextRepository(context);
        var signalContext = new SignalContext(
            signalId: signal.Id,
            channelId: 111111111,
            symbol: "BTCUSDT",
            currentState: SignalState.RECEIVED,
            lastAction: "Ingested",
            lastMessageId: 42
        );

        // Act - Save
        await contextRepo.CreateAsync(signalContext, CancellationToken.None);
        await context.SaveChangesAsync();

        // Act - Retrieve
        using var context2 = CreateDbContext();
        var contextRepo2 = new SignalContextRepository(context2);
        var retrieved = await contextRepo2.GetActiveContextAsync(111111111, "BTCUSDT", CancellationToken.None);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.Id.Should().Be(signalContext.Id);
        retrieved.SignalId.Should().Be(signal.Id);
        retrieved.Symbol.Should().Be("BTCUSDT");
        retrieved.CurrentState.Should().Be(SignalState.RECEIVED);
        retrieved.LastAction.Should().Be("Ingested");
        retrieved.LastMessageId.Should().Be(42);
    }

    [Fact]
    public async Task GetActiveContextAsync_ShouldReturnNull_WhenContextIsClosedOrCancelled()
    {
        // Arrange
        using var context = CreateDbContext();
        var signal = new Signal("TELEGRAM", "BUY BTCUSDT @ 45000", "BTCUSDT", OrderSide.Buy, 45000m, 1m);
        context.Signals.Add(signal);
        await context.SaveChangesAsync();

        var contextRepo = new SignalContextRepository(context);
        var signalContext = new SignalContext(signal.Id, 111111111, "BTCUSDT", SignalState.CLOSED, "Closed", 42);

        await contextRepo.CreateAsync(signalContext, CancellationToken.None);
        await context.SaveChangesAsync();

        // Act
        var retrieved = await contextRepo.GetActiveContextAsync(111111111, "BTCUSDT", CancellationToken.None);

        // Assert
        retrieved.Should().BeNull();
    }

    [Fact]
    public async Task UpdateStateAsync_ShouldUpdateStateAndLastAction()
    {
        // Arrange
        using var context = CreateDbContext();
        var signal = new Signal("TELEGRAM", "BUY BTCUSDT @ 45000", "BTCUSDT", OrderSide.Buy, 45000m, 1m);
        context.Signals.Add(signal);
        await context.SaveChangesAsync();

        var contextRepo = new SignalContextRepository(context);
        var signalContext = new SignalContext(signal.Id, 111111111, "BTCUSDT", SignalState.RECEIVED, "Ingested", 42);
        await contextRepo.CreateAsync(signalContext, CancellationToken.None);
        await context.SaveChangesAsync();

        // Act
        await contextRepo.UpdateStateAsync(signalContext.Id, SignalState.ACTIVE, "Activated", 43, CancellationToken.None);
        await context.SaveChangesAsync();

        // Assert
        using var context2 = CreateDbContext();
        var contextRepo2 = new SignalContextRepository(context2);
        var retrieved = await contextRepo2.GetActiveContextAsync(111111111, "BTCUSDT", CancellationToken.None);
        retrieved.Should().NotBeNull();
        retrieved!.CurrentState.Should().Be(SignalState.ACTIVE);
        retrieved.LastAction.Should().Be("Activated");
        retrieved.LastMessageId.Should().Be(43);
        retrieved.UpdatedAt.Should().NotBeNull();
    }
}
