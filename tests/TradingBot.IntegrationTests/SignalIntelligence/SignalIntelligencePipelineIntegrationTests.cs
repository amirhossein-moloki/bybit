using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradingBot.Application.SignalIntelligence.Configuration;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Application.SignalIntelligence.Parser;
using TradingBot.Application.SignalIntelligence.Validation;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Entities;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Domain.SignalIntelligence.Enums;
using TradingBot.Domain.SignalIntelligence.Events;
using TradingBot.Domain.SignalIntelligence.Interfaces;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;
using TradingBot.Parser.Parsers;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.SignalIntelligence.Repositories;
using TradingBot.Persistence.UnitOfWork;
using Xunit;

namespace TradingBot.IntegrationTests.SignalIntelligence;

public class SignalIntelligencePipelineIntegrationTests : IAsyncLifetime
{
    private SqliteConnection? _sqliteConnection;
    private TradingDbContext? _dbContext;
    private IUnitOfWork? _unitOfWork;

    private MessagePreprocessor _preprocessor = null!;
    private MessageClassifier _classifier = null!;
    private MessageAnalysisRepository _analysisRepository = null!;
    private MessageRepository _messageRepository = null!;
    private MessageProcessingTrackerRepository _trackerRepository = null!;
    private FailedMessageAnalysisRepository _failedRepository = null!;

    private Mock<IIntelligenceEventPublisher> _eventPublisherMock = null!;
    private Mock<ISignalParser> _signalParserMock = null!;

    private SignalValidationService _validationService = null!;
    private MessageParser _parser = null!;

    public async Task InitializeAsync()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        await _sqliteConnection.OpenAsync();

        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        _dbContext = new TradingDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();

        _unitOfWork = new UnitOfWork(_dbContext, NullLogger<UnitOfWork>.Instance);

        _preprocessor = new MessagePreprocessor();
        _classifier = new MessageClassifier(_preprocessor);

        _analysisRepository = new MessageAnalysisRepository(_dbContext);
        _messageRepository = new MessageRepository(_dbContext);
        _trackerRepository = new MessageProcessingTrackerRepository(_dbContext);
        _failedRepository = new FailedMessageAnalysisRepository(_dbContext);

        _eventPublisherMock = new Mock<IIntelligenceEventPublisher>();
        _signalParserMock = new Mock<ISignalParser>();

        var siOptions = new SignalIntelligenceOptions { MinimumConfidence = 0.85m };
        _validationService = new SignalValidationService(Microsoft.Extensions.Options.Options.Create(siOptions));

        _parser = new MessageParser(
            _preprocessor,
            _classifier,
            _analysisRepository,
            _messageRepository,
            _eventPublisherMock.Object,
            _unitOfWork,
            NullLogger<MessageParser>.Instance,
            null,
            null,
            null,
            null,
            null,
            _signalParserMock.Object,
            _validationService,
            _trackerRepository,
            _failedRepository,
            null,
            Microsoft.Extensions.Options.Options.Create(siOptions)
        );
    }

    public async Task DisposeAsync()
    {
        if (_dbContext != null)
        {
            await _dbContext.DisposeAsync();
        }
        if (_sqliteConnection != null)
        {
            await _sqliteConnection.CloseAsync();
            await _sqliteConnection.DisposeAsync();
        }
    }

    [Fact]
    public async Task E2EPipeline_WithValidSignal_ShouldSucceedAndPersistAllStagesAndEmitEvents()
    {
        // Arrange
        var content = "BTCUSDT BUY\nEntry: 60000\nSL: 59000\nTP: 62000";
        var message = new TelegramMessage(100L, 200L, null, content, DateTime.UtcNow);

        await _messageRepository.CreateAsync(message, CancellationToken.None);
        await _unitOfWork!.SaveChangesAsync(CancellationToken.None);

        var parsedSignal = new ParsedSignal
        {
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            EntryPrice = 60000m,
            StopLoss = 59000m,
            TakeProfits = new List<decimal> { 62000m }
        };

        _signalParserMock
            .Setup(p => p.ParseAsync(It.IsAny<ParserContext>()))
            .ReturnsAsync(ParserResult.SuccessResult(parsedSignal, "1.0"));

        // Act
        var result = await _parser.ParseAsync(message);

        // Assert
        result.Should().NotBeNull();
        result.Type.Should().Be(MessageType.SIGNAL);
        result.Symbol.Should().Be("BTCUSDT");
        result.Confidence.Should().Be(1.0m);

        // Verify state is PUBLISHED
        var tracker = await _trackerRepository.GetByTelegramMessageIdAsync(message.Id);
        tracker.Should().NotBeNull();
        tracker!.State.Should().Be("PUBLISHED");

        // Verify that MessageAnalysis has been created
        var analysis = await _analysisRepository.GetByMessageIdAsync(message.Id);
        analysis.Should().NotBeNull();
        analysis!.MessageType.Should().Be(MessageType.SIGNAL);

        // Verify Event publishing was called with the new SignalIntelligenceCreated event
        _eventPublisherMock.Verify(p => p.PublishAsync(It.IsAny<SignalIntelligenceCreated>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task E2EPipeline_WithInvalidSignal_ShouldRecordFailureInFailedMessageAnalysesAndStateShouldBeFailed()
    {
        // Arrange
        var content = "BTCUSDT BUY\nEntry: 60000"; // Missing SL/TP might fail rules or we simulate parser error
        var message = new TelegramMessage(100L, 200L, null, content, DateTime.UtcNow);

        await _messageRepository.CreateAsync(message, CancellationToken.None);
        await _unitOfWork!.SaveChangesAsync(CancellationToken.None);

        _signalParserMock
            .Setup(p => p.ParseAsync(It.IsAny<ParserContext>()))
            .ThrowsAsync(new Exception("Simulated Parser Crash"));

        // Act
        var result = await _parser.ParseAsync(message);

        // Assert
        result.Should().NotBeNull();
        result.Type.Should().Be(MessageType.UNKNOWN);

        // Verify tracker is FAILED
        var tracker = await _trackerRepository.GetByTelegramMessageIdAsync(message.Id);
        tracker.Should().NotBeNull();
        tracker!.State.Should().Be("FAILED");

        // Verify FailedMessageAnalysis is stored
        var failedRecord = await _failedRepository.GetByMessageIdAsync(message.Id);
        failedRecord.Should().NotBeNull();
        failedRecord!.FailureReason.Should().Contain("Simulated Parser Crash");
    }
}
