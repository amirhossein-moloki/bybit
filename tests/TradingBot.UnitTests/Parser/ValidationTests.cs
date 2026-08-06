using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Parser.Configuration;
using TradingBot.Parser.Models;
using TradingBot.Parser.Validation;
using TradingBot.Parser.Validation.Rules;
using Xunit;

namespace TradingBot.UnitTests.Parser;

public class ValidationTests
{
    private readonly Mock<IRepository<Symbol>> _symbolRepoMock = new();
    private readonly Mock<ISignalRepository> _signalRepoMock = new();
    private readonly Mock<IUnitOfWork> _uowMock = new();
    private readonly Mock<ILogger<ValidationEngine>> _loggerMock = new();
    private readonly IOptions<ValidationOptions> _defaultOptions = Options.Create(new ValidationOptions
    {
        RequireStopLoss = true,
        RequireTakeProfit = true,
        MaximumLeverage = 100,
        RejectUnknownSymbols = true
    });

    public ValidationTests()
    {
        // Setup supported symbols in repository mock
        var btc = new Symbol("BYBIT", "BTCUSDT", "BTC", "USDT", 0.1m, 0.001m, 0.0001m);
        var eth = new Symbol("BYBIT", "ETHUSDT", "ETH", "USDT", 0.01m, 0.01m, 0.001m);
        _symbolRepoMock.Setup(r => r.GetAllAsync(default))
            .ReturnsAsync(new List<Symbol> { btc, eth });
    }

    private ValidationEngine CreateEngine(params IValidationRule[] rules)
    {
        IEnumerable<IValidationRule> rulesList = rules.Length > 0 ? rules : GetDefaultRules();
        return new ValidationEngine(rulesList, _signalRepoMock.Object, _uowMock.Object, _loggerMock.Object);
    }

    private List<IValidationRule> GetDefaultRules()
    {
        return new List<IValidationRule>
        {
            new SymbolValidationRule(_symbolRepoMock.Object, _defaultOptions),
            new DirectionValidationRule(),
            new EntryValidationRule(),
            new StopLossValidationRule(_defaultOptions),
            new TakeProfitValidationRule(_defaultOptions),
            new LeverageValidationRule(_defaultOptions),
            new BusinessConsistencyValidationRule()
        };
    }

    [Fact]
    public async Task ValidateAsync_WithValidLongSignal_ShouldReturnValidatedStatus()
    {
        // Arrange
        var engine = CreateEngine();
        var signal = new Signal("Telegram", "RawMessage", "BTCUSDT", OrderSide.Buy, 50000m, 1m);
        var parsedSignal = new ParsedSignal
        {
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            EntryPrice = 50000m,
            StopLoss = 49000m,
            Leverage = 10
        };
        parsedSignal.TakeProfits.Add(52000m);

        // Act
        var result = await engine.ValidateAsync(signal, parsedSignal);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ValidationStatus.Should().Be("Validated");
        result.Errors.Should().BeEmpty();
        signal.Status.Should().Be(SignalStatus.ReadyForRiskEngine);
        signal.ValidationStatus.Should().Be("Validated");
    }

    [Fact]
    public async Task ValidateAsync_WithValidShortSignal_ShouldReturnValidatedStatus()
    {
        // Arrange
        var engine = CreateEngine();
        var signal = new Signal("Telegram", "RawMessage", "ETHUSDT", OrderSide.Sell, 3000m, 1m);
        var parsedSignal = new ParsedSignal
        {
            Symbol = "ETHUSDT",
            Side = OrderSide.Sell,
            EntryPrice = 3000m,
            StopLoss = 3100m,
            Leverage = 20
        };
        parsedSignal.TakeProfits.Add(2900m);

        // Act
        var result = await engine.ValidateAsync(signal, parsedSignal);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ValidationStatus.Should().Be("Validated");
        result.Errors.Should().BeEmpty();
        signal.Status.Should().Be(SignalStatus.ReadyForRiskEngine);
        signal.ValidationStatus.Should().Be("Validated");
    }

    [Fact]
    public async Task ValidateAsync_WithMissingStopLoss_AndRequired_ShouldReturnRejected()
    {
        // Arrange
        var engine = CreateEngine();
        var signal = new Signal("Telegram", "RawMessage", "BTCUSDT", OrderSide.Buy, 50000m, 1m);
        var parsedSignal = new ParsedSignal
        {
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            EntryPrice = 50000m,
            StopLoss = null,
            Leverage = 10
        };
        parsedSignal.TakeProfits.Add(52000m);

        // Act
        var result = await engine.ValidateAsync(signal, parsedSignal);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationStatus.Should().Be("Rejected");
        result.Errors.Should().Contain(e => e.Contains("Stop loss is required but missing"));
        signal.Status.Should().Be(SignalStatus.Rejected);
        signal.ValidationStatus.Should().Be("Rejected");
    }

    [Fact]
    public async Task ValidateAsync_WithInvalidSymbol_ShouldReturnRejected()
    {
        // Arrange
        var engine = CreateEngine();
        var signal = new Signal("Telegram", "RawMessage", "INVALID", OrderSide.Buy, 50000m, 1m);
        var parsedSignal = new ParsedSignal
        {
            Symbol = "INVALID",
            Side = OrderSide.Buy,
            EntryPrice = 50000m,
            StopLoss = 49000m,
            Leverage = 10
        };
        parsedSignal.TakeProfits.Add(52000m);

        // Act
        var result = await engine.ValidateAsync(signal, parsedSignal);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationStatus.Should().Be("Rejected");
        result.Errors.Should().Contain(e => e.Contains("is not a supported trading pair"));
        signal.Status.Should().Be(SignalStatus.Rejected);
    }

    [Fact]
    public async Task ValidateAsync_WithInvalidEntryPrice_ShouldReturnRejected()
    {
        // Arrange
        var engine = CreateEngine();
        var signal = new Signal("Telegram", "RawMessage", "BTCUSDT", OrderSide.Buy, 50000m, 1m);
        var parsedSignal = new ParsedSignal
        {
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            EntryPrice = -500m,
            StopLoss = 49000m,
            Leverage = 10
        };
        parsedSignal.TakeProfits.Add(52000m);

        // Act
        var result = await engine.ValidateAsync(signal, parsedSignal);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationStatus.Should().Be("Rejected");
        result.Errors.Should().Contain(e => e.Contains("Entry price must be positive"));
        signal.Status.Should().Be(SignalStatus.Rejected);
    }

    [Fact]
    public async Task ValidateAsync_WithInvalidTakeProfit_ShouldReturnRejected()
    {
        // Arrange
        var engine = CreateEngine();
        var signal = new Signal("Telegram", "RawMessage", "BTCUSDT", OrderSide.Buy, 50000m, 1m);
        var parsedSignal = new ParsedSignal
        {
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            EntryPrice = 50000m,
            StopLoss = 49000m,
            Leverage = 10
        };
        parsedSignal.TakeProfits.Add(-100m);

        // Act
        var result = await engine.ValidateAsync(signal, parsedSignal);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationStatus.Should().Be("Rejected");
        result.Errors.Should().Contain(e => e.Contains("Take profit target must be positive"));
        signal.Status.Should().Be(SignalStatus.Rejected);
    }

    [Fact]
    public async Task ValidateAsync_WithValidLeverage_ShouldReturnValidated()
    {
        // Arrange
        var engine = CreateEngine();
        var signal = new Signal("Telegram", "RawMessage", "BTCUSDT", OrderSide.Buy, 50000m, 1m);
        var parsedSignal = new ParsedSignal
        {
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            EntryPrice = 50000m,
            StopLoss = 49000m,
            Leverage = 50
        };
        parsedSignal.TakeProfits.Add(52000m);

        // Act
        var result = await engine.ValidateAsync(signal, parsedSignal);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ValidationStatus.Should().Be("Validated");
    }

    [Fact]
    public async Task ValidateAsync_WithInvalidLeverage_ShouldReturnRejected()
    {
        // Arrange
        var engine = CreateEngine();
        var signal = new Signal("Telegram", "RawMessage", "BTCUSDT", OrderSide.Buy, 50000m, 1m);
        var parsedSignal = new ParsedSignal
        {
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            EntryPrice = 50000m,
            StopLoss = 49000m,
            Leverage = 150
        };
        parsedSignal.TakeProfits.Add(52000m);

        // Act
        var result = await engine.ValidateAsync(signal, parsedSignal);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationStatus.Should().Be("Rejected");
        result.Errors.Should().Contain(e => e.Contains("exceeds maximum configured limit"));
    }

    [Fact]
    public async Task ValidateAsync_WithMissingLeverage_ShouldNotBeRejected()
    {
        // Arrange
        var engine = CreateEngine();
        var signal = new Signal("Telegram", "RawMessage", "BTCUSDT", OrderSide.Buy, 50000m, 1m);
        var parsedSignal = new ParsedSignal
        {
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            EntryPrice = 50000m,
            StopLoss = 49000m,
            Leverage = null
        };
        parsedSignal.TakeProfits.Add(52000m);

        // Act
        var result = await engine.ValidateAsync(signal, parsedSignal);

        // Assert
        result.IsValid.Should().BeTrue();
        result.ValidationStatus.Should().Be("Validated");
    }

    [Fact]
    public async Task ValidateAsync_WithBusinessConsistencyViolation_LongStopLoss_ShouldReturnRejected()
    {
        // Arrange
        var engine = CreateEngine();
        var signal = new Signal("Telegram", "RawMessage", "BTCUSDT", OrderSide.Buy, 50000m, 1m);
        var parsedSignal = new ParsedSignal
        {
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            EntryPrice = 50000m,
            StopLoss = 51000m,
            Leverage = 10
        };
        parsedSignal.TakeProfits.Add(52000m);

        // Act
        var result = await engine.ValidateAsync(signal, parsedSignal);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationStatus.Should().Be("Rejected");
        result.Errors.Should().Contain(e => e.Contains("Stop Loss") && e.Contains("must be less than Entry Price"));
    }
}
