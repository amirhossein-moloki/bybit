using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Parser.Configuration;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;
using TradingBot.Parser.Parsers;
using TradingBot.Parser.Pipeline;
using TradingBot.Parser.Validation;
using TradingBot.Parser.Validation.Rules;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;
using TradingBot.Persistence.UnitOfWork;
using Xunit;

namespace TradingBot.IntegrationTests.Parser;

public class FakeSymbolRepository : RepositoryBase<TradingBot.Domain.Entities.Symbol>
{
    public FakeSymbolRepository(TradingDbContext dbContext) : base(dbContext)
    {
    }
}

public class ValidationIntegrationTests : IAsyncLifetime
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

    private (TradingDbContext context, ISignalParser parser, ISignalValidator validator) CreateServices()
    {
        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseSqlite(_sqliteConnection!)
            .Options;

        var context = new TradingDbContext(options);
        context.Database.EnsureCreated();

        // Seed some default symbols
        if (!context.Symbols.Any())
        {
            context.Symbols.AddRange(
                new TradingBot.Domain.Entities.Symbol("BYBIT", "BTCUSDT", "BTC", "USDT", 0.1m, 0.001m, 0.0001m),
                new TradingBot.Domain.Entities.Symbol("BYBIT", "ETHUSDT", "ETH", "USDT", 0.01m, 0.01m, 0.001m)
            );
            context.SaveChanges();
        }

        var validationOptions = Options.Create(new ValidationOptions
        {
            RequireStopLoss = true,
            RequireTakeProfit = true,
            MaximumLeverage = 100,
            RejectUnknownSymbols = true
        });

        var parserOptions = Options.Create(new ParserOptions
        {
            Version = "1.0.0",
            MaxMessageLength = 5000
        });

        var symbolRepo = new FakeSymbolRepository(context);
        var signalRepo = new SignalRepository(context);
        var uow = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);

        var extractors = new List<ISignalExtractor>
        {
            new TradingBot.Parser.Extractors.SymbolExtractor(),
            new TradingBot.Parser.Extractors.DirectionExtractor(),
            new TradingBot.Parser.Extractors.EntryExtractor(),
            new TradingBot.Parser.Extractors.StopLossExtractor(),
            new TradingBot.Parser.Extractors.TakeProfitExtractor(),
            new TradingBot.Parser.Extractors.LeverageExtractor()
        };

        var pipeline = new SignalParserPipeline(extractors, parserOptions, NullLogger<SignalParserPipeline>.Instance);
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

        return (context, parser, validator);
    }

    [Fact]
    public async Task ProcessFlow_WithValidRawSignal_ShouldParseValidateAndSaveSuccessfully()
    {
        // 1. Database Signal: Setup raw signal in database
        var (context, parser, validator) = CreateServices();
        var rawMessage = "BTCUSDT LONG\nEntry: 45000\nSL: 44000\nTP: 48000\nLeverage: 10";
        var signal = new Signal(123456, 7890, rawMessage, "BTCUSDT", OrderSide.Buy, DateTime.UtcNow);

        context.Signals.Add(signal);
        await context.SaveChangesAsync();

        // 2. Parser: Execute signal parsing
        signal.MarkParsing();
        var parserContext = new ParserContext(signal.Id, signal.RawMessage, "123456", signal.CreatedAt, "1.0.0");
        var parserResult = await parser.ParseAsync(parserContext);

        parserResult.Success.Should().BeTrue();
        parserResult.ParsedSignal.Should().NotBeNull();
        signal.MarkParsed();
        await context.SaveChangesAsync();

        // 3. Validation Engine: Validate parsed signal and transition status
        var validationResult = await validator.ValidateAsync(signal, parserResult.ParsedSignal!);

        // 4. Signal Status Updated & 5. Database Verification
        validationResult.IsValid.Should().BeTrue();
        validationResult.ValidationStatus.Should().Be("Validated");

        // Reload from database to verify persistence
        using var checkContext = new TradingDbContext(new DbContextOptionsBuilder<TradingDbContext>().UseSqlite(_sqliteConnection!).Options);
        var dbSignal = await checkContext.Signals.FindAsync(signal.Id);

        dbSignal.Should().NotBeNull();
        dbSignal!.Status.Should().Be(SignalStatus.ReadyForRiskEngine);
        dbSignal.ValidationStatus.Should().Be("Validated");
        dbSignal.ValidationMessage.Should().BeEmpty();
        dbSignal.Symbol.Should().Be("BTCUSDT");
        dbSignal.Side.Should().Be(OrderSide.Buy);
        dbSignal.EntryPrice.Should().Be(45000m);
        dbSignal.StopLoss.Should().Be(44000m);
        dbSignal.TakeProfit.Should().Be(48000m);
        dbSignal.Leverage.Should().Be(10);
        dbSignal.ValidatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ProcessFlow_WithInvalidRawSignal_ShouldParseValidateAndMarkRejected()
    {
        var (context, parser, validator) = CreateServices();
        var rawMessage = "BTCUSDT LONG\nEntry: 45000\nSL: 46000\nTP: 43000\nLeverage: 10"; // Bad SL/TP consistency
        var signal = new Signal(123456, 7890, rawMessage, "BTCUSDT", OrderSide.Buy, DateTime.UtcNow);

        context.Signals.Add(signal);
        await context.SaveChangesAsync();

        signal.MarkParsing();
        var parserContext = new ParserContext(signal.Id, signal.RawMessage, "123456", signal.CreatedAt, "1.0.0");
        var parserResult = await parser.ParseAsync(parserContext);

        parserResult.Success.Should().BeTrue();
        signal.MarkParsed();
        await context.SaveChangesAsync();

        var validationResult = await validator.ValidateAsync(signal, parserResult.ParsedSignal!);

        validationResult.IsValid.Should().BeFalse();
        validationResult.ValidationStatus.Should().Be("Rejected");

        // Reload and verify
        using var checkContext = new TradingDbContext(new DbContextOptionsBuilder<TradingDbContext>().UseSqlite(_sqliteConnection!).Options);
        var dbSignal = await checkContext.Signals.FindAsync(signal.Id);

        dbSignal.Should().NotBeNull();
        dbSignal!.Status.Should().Be(SignalStatus.Rejected);
        dbSignal.ValidationStatus.Should().Be("Rejected");
        dbSignal.ValidationMessage.Should().Contain("Stop Loss");
    }
}
