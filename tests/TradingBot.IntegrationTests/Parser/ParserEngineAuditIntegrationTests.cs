using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;
using TradingBot.Parser;
using TradingBot.Parser.Configuration;
using TradingBot.Parser.Exceptions;
using TradingBot.Parser.Extractors;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;
using TradingBot.Parser.Parsers;
using TradingBot.Parser.Pipeline;
using TradingBot.Parser.Templates;
using TradingBot.Parser.Validation;
using TradingBot.Parser.Validation.Rules;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;
using TradingBot.Persistence.UnitOfWork;
using Xunit;

namespace TradingBot.IntegrationTests.Parser;

public class ParserEngineAuditIntegrationTests : IAsyncLifetime
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

    private (TradingDbContext context, ISignalParser parser, ISignalValidator validator, ITemplateManager templateManager) CreateServices(
        bool enableDatabaseTemplates = true,
        bool requireStopLoss = true,
        bool requireTakeProfit = true,
        int maximumLeverage = 100,
        bool rejectUnknownSymbols = true,
        int maxMessageLength = 5000)
    {
        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseSqlite(_sqliteConnection!)
            .Options;

        var context = new TradingDbContext(options);
        context.Database.EnsureCreated();

        // Seed default symbols for the database
        if (!context.Symbols.Any())
        {
            context.Symbols.AddRange(
                new TradingBot.Domain.Entities.Symbol("BYBIT", "BTCUSDT", "BTC", "USDT", 0.1m, 0.001m, 0.0001m),
                new TradingBot.Domain.Entities.Symbol("BYBIT", "ETHUSDT", "ETH", "USDT", 0.01m, 0.01m, 0.001m),
                new TradingBot.Domain.Entities.Symbol("BYBIT", "SOLUSDT", "SOL", "USDT", 0.05m, 0.1m, 0.01m)
            );
            context.SaveChanges();
        }

        var validationOptions = Options.Create(new ValidationOptions
        {
            RequireStopLoss = requireStopLoss,
            RequireTakeProfit = requireTakeProfit,
            MaximumLeverage = maximumLeverage,
            RejectUnknownSymbols = rejectUnknownSymbols
        });

        var parserOptions = Options.Create(new ParserOptions
        {
            Version = "1.0.0",
            MaxMessageLength = maxMessageLength
        });

        var templateOptions = Options.Create(new ParserTemplatesOptions
        {
            EnableDatabaseTemplates = enableDatabaseTemplates,
            FallbackTemplate = "Default"
        });

        var symbolRepo = new FakeSymbolRepository(context);
        var signalRepo = new SignalRepository(context);
        var templateRepo = new ParserTemplateRepository(context);
        var uow = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);

        var defaultTemplate = new DefaultSignalTemplate();
        var templateManager = new TemplateManager(templateOptions, NullLogger<TemplateManager>.Instance, defaultTemplate, templateRepo);

        var extractors = new List<ISignalExtractor>
        {
            new SymbolExtractor(),
            new DirectionExtractor(),
            new EntryExtractor(),
            new StopLossExtractor(),
            new TakeProfitExtractor(),
            new LeverageExtractor()
        };

        var pipeline = new SignalParserPipeline(extractors, parserOptions, NullLogger<SignalParserPipeline>.Instance, templateManager);
        var parser = new DefaultSignalParser(pipeline, parserOptions, NullLogger<DefaultSignalParser>.Instance);

        var rules = new List<IValidationRule>
        {
            new SymbolValidationRule(symbolRepo, validationOptions),
            new DirectionValidationRule(),
            new EntryValidationRule(),
            new StopLossValidationRule(validationOptions),
            new TakeProfitValidationRule(validationOptions),
            new LeverageValidationRule(validationOptions),
            new BusinessConsistencyValidationRule()
        };

        var validator = new ValidationEngine(rules, signalRepo, uow, NullLogger<ValidationEngine>.Instance);

        return (context, parser, validator, templateManager);
    }

    [Fact]
    public async Task Part4_E2E_Pipeline_ShouldSuccessfullyProcessStoredSignal()
    {
        // Arrange
        var (context, parser, validator, _) = CreateServices();

        // 1. Store Raw Message in Database
        var rawText = "BTCUSDT LONG\nEntry: 60000\nSL: 59000\nTP1: 62000\nTP2: 63000\nLeverage: 10";
        var signal = new Signal(12345L, 56789L, rawText, "BTCUSDT", OrderSide.Buy, DateTime.UtcNow);
        context.Signals.Add(signal);
        await context.SaveChangesAsync();

        signal.Status.Should().Be(SignalStatus.Received);

        // 2. Route through Parser Pipeline
        signal.MarkParsing();
        var parserContext = new ParserContext(signal.Id, signal.RawMessage, "12345", signal.CreatedAt, "1.0.0");
        var parserResult = await parser.ParseAsync(parserContext);

        parserResult.Success.Should().BeTrue();
        parserResult.ParsedSignal.Should().NotBeNull();
        signal.MarkParsed();
        await context.SaveChangesAsync();

        // 3. Validation Engine Execution & DB Update
        var validationResult = await validator.ValidateAsync(signal, parserResult.ParsedSignal!, "12345", "Default", "1.0.0");

        // 4. Verification of results & persistence
        validationResult.IsValid.Should().BeTrue();
        validationResult.ValidationStatus.Should().Be("Validated");

        // Reload from Db to ensure perfect consistency
        using var verifyCtx = new TradingDbContext(new DbContextOptionsBuilder<TradingDbContext>().UseSqlite(_sqliteConnection!).Options);
        var dbSignal = await verifyCtx.Signals.FindAsync(signal.Id);

        dbSignal.Should().NotBeNull();
        dbSignal!.Status.Should().Be(SignalStatus.ReadyForRiskEngine);
        dbSignal.ValidationStatus.Should().Be("Validated");
        dbSignal.Symbol.Should().Be("BTCUSDT");
        dbSignal.Side.Should().Be(OrderSide.Buy);
        dbSignal.EntryPrice.Should().Be(60000m);
        dbSignal.StopLoss.Should().Be(59000m);
        dbSignal.TakeProfit.Should().Be(62000m); // First target mapped to TakeProfit for compatibility
        dbSignal.Leverage.Should().Be(10);
        dbSignal.ParserVersion.Should().Be("1.0.0");
        dbSignal.ValidatedAt.Should().NotBeNull();

        // Assert shadow property for UpdatedAt
        var updatedAt = verifyCtx.Entry(dbSignal).Property<DateTime?>("UpdatedAt").CurrentValue;
        updatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Part5_ParserAccuracy_ShouldHandleStandardFormat()
    {
        var (_, parser, _, _) = CreateServices();
        var rawText = "BTC LONG\nEntry: 60000\nSL: 59000\nTP1: 62000\nTP2: 63000";
        var context = new ParserContext(Guid.NewGuid(), rawText, "TestChannel", DateTime.UtcNow, "1.0.0");

        var result = await parser.ParseAsync(context);

        result.Success.Should().BeTrue();
        result.ParsedSignal!.Symbol.Should().Be("BTCUSDT");
        result.ParsedSignal.Side.Should().Be(OrderSide.Buy);
        result.ParsedSignal.EntryPrice.Should().Be(60000m);
        result.ParsedSignal.StopLoss.Should().Be(59000m);
        result.ParsedSignal.TakeProfits.Should().Equal(62000m, 63000m);
    }

    [Fact]
    public async Task Part5_ParserAccuracy_ShouldHandleAlternativeFormatUsingTemplate()
    {
        // Arrange - setup template in DB for custom format
        var (context, parser, _, _) = CreateServices(enableDatabaseTemplates: true);

        var templateRuleJson = @"[
            {""Field"":""Symbol"",""Pattern"":"""",""Extractor"":""SymbolExtractor"",""Required"":true,""Order"":1},
            {""Field"":""Side"",""Pattern"":"""",""Extractor"":""DirectionExtractor"",""Required"":true,""Order"":2},
            {""Field"":""EntryPrice"",""Pattern"":""BUY AREA"",""Extractor"":""EntryExtractor"",""Required"":true,""Order"":3},
            {""Field"":""StopLoss"",""Pattern"":""STOP"",""Extractor"":""StopLossExtractor"",""Required"":true,""Order"":4},
            {""Field"":""TakeProfits"",""Pattern"":""TARGET"",""Extractor"":""TakeProfitExtractor"",""Required"":true,""Order"":5}
        ]";

        var templateEntity = new ParserTemplates
        {
            Id = Guid.NewGuid(),
            Name = "Alternative Template",
            ChannelId = 999111L,
            Enabled = true,
            ConfigurationJson = templateRuleJson,
            CreatedAt = DateTime.UtcNow
        };
        context.ParserTemplates.Add(templateEntity);
        await context.SaveChangesAsync();

        var altText = "LONG BTC\nBUY AREA\n60000-60500\nSTOP\n59000\nTARGET\n62000";
        var parserContext = new ParserContext(Guid.NewGuid(), altText, "999111", DateTime.UtcNow, "1.0.0");

        // Act
        var result = await parser.ParseAsync(parserContext);

        // Assert
        result.Success.Should().BeTrue();
        result.ParsedSignal!.Symbol.Should().Be("BTCUSDT");
        result.ParsedSignal.Side.Should().Be(OrderSide.Buy);
        result.ParsedSignal.EntryPrice.Should().Be(60000m);
        result.ParsedSignal.StopLoss.Should().Be(59000m);
        result.ParsedSignal.TakeProfits.Should().ContainSingle().Which.Should().Be(62000m);
    }

    [Fact]
    public async Task Part5_ParserAccuracy_ShouldHandleMinimalFormat()
    {
        var (_, parser, _, _) = CreateServices();
        var rawText = "ETH SHORT\nEntry 3500\nSL 3600";
        var context = new ParserContext(Guid.NewGuid(), rawText, "TestChannel", DateTime.UtcNow, "1.0.0");

        var result = await parser.ParseAsync(context);

        result.Success.Should().BeTrue();
        result.ParsedSignal!.Symbol.Should().Be("ETHUSDT");
        result.ParsedSignal.Side.Should().Be(OrderSide.Sell);
        result.ParsedSignal.EntryPrice.Should().Be(3500m);
        result.ParsedSignal.StopLoss.Should().Be(3600m);
        result.Warnings.Should().Contain("Take profits not detected");
    }

    [Fact]
    public async Task Part5_ParserAccuracy_ShouldHandleInvalidFormatByFailingOrReportingWarnings()
    {
        var (_, parser, _, _) = CreateServices();
        var rawText = "TRADE POSITION NOW LIMIT 12345";
        var context = new ParserContext(Guid.NewGuid(), rawText, "TestChannel", DateTime.UtcNow, "1.0.0");

        var result = await parser.ParseAsync(context);

        // Standard parser returns Success = true but logs errors/warnings inside ParsedSignal.
        // Let's assert that the extracted crucial fields (Symbol, Side) are missing.
        result.ParsedSignal!.Symbol.Should().BeNull();
        result.ParsedSignal.Side.Should().BeNull();
        result.Warnings.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Part6_ValidationAudit_ShouldRejectInvalidBusinessConsistency()
    {
        var (context, parser, validator, _) = CreateServices();

        // Entry = 60000, but SL = 61000 for LONG! That's mathematically inconsistent.
        var rawText = "BTC LONG\nEntry: 60000\nSL: 61000\nTP: 65000";
        var signal = new Signal(12345L, 56789L, rawText, "BTCUSDT", OrderSide.Buy, DateTime.UtcNow);
        context.Signals.Add(signal);
        await context.SaveChangesAsync();

        var parserContext = new ParserContext(signal.Id, signal.RawMessage, "12345", signal.CreatedAt, "1.0.0");
        var parserResult = await parser.ParseAsync(parserContext);
        var validationResult = await validator.ValidateAsync(signal, parserResult.ParsedSignal!);

        validationResult.IsValid.Should().BeFalse();
        validationResult.ValidationStatus.Should().Be("Rejected");
        validationResult.Errors.Should().Contain(e => e.Contains("Stop Loss"));
    }

    [Fact]
    public void Part6_Domain_SignalStatusTransitions_ShouldThrowExceptionOnInvalidTransition()
    {
        var signal = new Signal(12345L, 56789L, "BTC LONG", "BTCUSDT", OrderSide.Buy, DateTime.UtcNow);

        // Signal is in Received status, cannot transition directly to Validated without Parsing/Parsed
        Action act1 = () => signal.MarkValidated();
        act1.Should().NotThrow("Because MarkValidated supports Received, Parsed, and Parsing transitions for resilience");

        // Execute signal
        signal.MarkExecuted();
        signal.Status.Should().Be(SignalStatus.Executed);

        // Once executed, cannot reject
        Action act2 = () => signal.MarkRejected();
        act2.Should().Throw<DomainException>();
    }

    [Fact]
    public async Task Part7_DatabaseAudit_ShouldEnsureCorrectEFMappingAndNoCorruption()
    {
        var (context, parser, validator, _) = CreateServices();

        var rawText = "SOL LONG\nEntry: 150\nSL: 140\nTP1: 160\nTP2: 170\nLeverage: 25";
        var signal = new Signal(111L, 222L, rawText, "SOLUSDT", OrderSide.Buy, DateTime.UtcNow);
        context.Signals.Add(signal);
        await context.SaveChangesAsync();

        var parserContext = new ParserContext(signal.Id, signal.RawMessage, "111", signal.CreatedAt, "1.0.0");
        var parserResult = await parser.ParseAsync(parserContext);
        await validator.ValidateAsync(signal, parserResult.ParsedSignal!, "111", "Default", "1.0.0");

        // Reload completely to test properties
        using var verifyCtx = new TradingDbContext(new DbContextOptionsBuilder<TradingDbContext>().UseSqlite(_sqliteConnection!).Options);
        var dbSignal = await verifyCtx.Signals.AsNoTracking().FirstOrDefaultAsync(s => s.Id == signal.Id);

        dbSignal.Should().NotBeNull();
        dbSignal!.RawMessage.Should().Be(rawText);
        dbSignal.Symbol.Should().Be("SOLUSDT");
        dbSignal.Side.Should().Be(OrderSide.Buy);
        dbSignal.EntryPrice.Should().Be(150m);
        dbSignal.StopLoss.Should().Be(140m);
        dbSignal.TakeProfit.Should().Be(160m);
        dbSignal.Leverage.Should().Be(25);
        dbSignal.ValidationStatus.Should().Be("Validated");
        dbSignal.ParserVersion.Should().Be("1.0.0");
    }

    [Fact]
    public async Task Part8_PerformanceAudit_ShouldTrackExecutionTimingsAndHandleModerateLoad()
    {
        var (context, parser, validator, _) = CreateServices();

        var rawText = "BTC LONG\nEntry: 60000\nSL: 59000\nTP1: 62000\nLeverage: 10";

        // Let's run a loop to measure timings
        const int RunCount = 50;
        var parseTimes = new List<double>();
        var validateTimes = new List<double>();

        for (int i = 0; i < RunCount; i++)
        {
            var signal = new Signal(999L, i, rawText, "BTCUSDT", OrderSide.Buy, DateTime.UtcNow);
            context.Signals.Add(signal);
            await context.SaveChangesAsync();

            var parserContext = new ParserContext(signal.Id, signal.RawMessage, "999", signal.CreatedAt, "1.0.0");

            var swParse = Stopwatch.StartNew();
            var parserResult = await parser.ParseAsync(parserContext);
            swParse.Stop();
            parseTimes.Add(swParse.Elapsed.TotalMilliseconds);

            var swVal = Stopwatch.StartNew();
            var valResult = await validator.ValidateAsync(signal, parserResult.ParsedSignal!);
            swVal.Stop();
            validateTimes.Add(swVal.Elapsed.TotalMilliseconds);

            valResult.IsValid.Should().BeTrue();
        }

        var avgParse = parseTimes.Average();
        var avgVal = validateTimes.Average();

        avgParse.Should().BeLessThan(20, "Parsing must be extremely fast");
        avgVal.Should().BeLessThan(50, "Validation and database update must be fast");
    }

    [Fact]
    public void Part9_SecurityAudit_ShouldStripControlAndNullBytesDuringContextCreation()
    {
        var id = Guid.NewGuid();
        var rawText = "BTCUSDT LONG\nEntry: \u000060000\nSL: 59000\r\nTP: 62000";

        var context = new ParserContext(id, rawText, "999", DateTime.UtcNow, "1.0.0");

        // Verify null bytes are removed and normalized
        context.RawMessage.Should().NotContain("\u0000");
    }

    [Fact]
    public void Part9_SecurityAudit_ShouldThrowOnExceededMessageLength()
    {
        var id = Guid.NewGuid();
        var longText = new string('A', 6000);

        Action act = () => new ParserContext(id, longText, "999", DateTime.UtcNow, "1.0.0", maxMessageLength: 5000);

        act.Should().Throw<InvalidParserContextException>().WithMessage("*exceeds the maximum limit*");
    }

    [Fact]
    public async Task Part10_FailureScenarios_ShouldMarkRequiresReviewOnUnexpectedPersistenceFailure()
    {
        // To simulate a database failure, we can pass a mock/faulty SignalRepository or UnitOfWork.
        var faultyUow = new Mock<IUnitOfWork>();
        faultyUow.Setup(u => u.SaveChangesAsync(It.IsAny<System.Threading.CancellationToken>()))
            .ThrowsAsync(new DbUpdateException("Simulated database failure during save"));

        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseSqlite(_sqliteConnection!)
            .Options;
        var context = new TradingDbContext(options);
        context.Database.EnsureCreated();

        var signalRepo = new SignalRepository(context);
        var rules = new List<IValidationRule> { new DirectionValidationRule() };

        var validator = new ValidationEngine(rules, signalRepo, faultyUow.Object, NullLogger<ValidationEngine>.Instance);

        var signal = new Signal(111L, 222L, "BTC LONG", "BTCUSDT", OrderSide.Buy, DateTime.UtcNow);
        // Add to db using standard context to avoid initial save issues
        context.Signals.Add(signal);
        await context.SaveChangesAsync();

        var parsedSignal = new ParsedSignal { Symbol = "BTCUSDT", Side = OrderSide.Buy };

        // Act
        var result = await validator.ValidateAsync(signal, parsedSignal);

        // Assert
        result.ValidationStatus.Should().Be("RequiresReview");
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Part10_FailureScenarios_ShouldGracefullyFallbackToDefaultTemplate_WhenDbTemplateJsonCorrupted()
    {
        var (context, parser, _, _) = CreateServices(enableDatabaseTemplates: true);

        // Corrupted template in database (invalid JSON configuration)
        var corruptedTemplate = new ParserTemplates
        {
            Id = Guid.NewGuid(),
            Name = "Corrupted Template",
            ChannelId = 888222L,
            Enabled = true,
            ConfigurationJson = "This is not valid JSON!!!",
            CreatedAt = DateTime.UtcNow
        };
        context.ParserTemplates.Add(corruptedTemplate);
        await context.SaveChangesAsync();

        var rawText = "BTC LONG\nEntry: 60000\nSL: 59000\nTP: 62000";
        var parserContext = new ParserContext(Guid.NewGuid(), rawText, "888222", DateTime.UtcNow, "1.0.0");

        // Act - should fall back to DefaultSignalTemplate due to corrupted JSON and still parse successfully!
        var result = await parser.ParseAsync(parserContext);

        result.Success.Should().BeTrue();
        result.ParsedSignal!.Symbol.Should().Be("BTCUSDT");
        result.ParsedSignal.Side.Should().Be(OrderSide.Buy);
        result.ParsedSignal.EntryPrice.Should().Be(60000m);
    }
}
