using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Enums;
using TradingBot.Application.Trading.Execution.Exceptions;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Application.Trading.Execution.Services;
using TradingBot.Domain.Enums;
using TradingBot.Domain.RiskManagement.Enums;
using Xunit;

namespace TradingBot.UnitTests.Execution;

public class TradingExecutionTests
{
    private readonly Mock<ILogger<TradingExecutionService>> _loggerMock;
    private readonly IOrderValidator _validator;
    private readonly IOrderBuilder _builder;
    private readonly TestExchangeInstrumentRules _instrumentRules;
    private readonly Mock<IExchangeTradingGateway> _gatewayMock;

    public TradingExecutionTests()
    {
        _loggerMock = new Mock<ILogger<TradingExecutionService>>();
        _validator = new OrderValidator();
        _builder = new OrderBuilder();
        _instrumentRules = new TestExchangeInstrumentRules();
        _gatewayMock = new Mock<IExchangeTradingGateway>();
    }

    #region Order Builder Tests

    [Theory]
    [InlineData(OrderSide.Buy, OrderType.Market)]
    [InlineData(OrderSide.Sell, OrderType.Market)]
    [InlineData(OrderSide.Buy, OrderType.Limit)]
    [InlineData(OrderSide.Sell, OrderType.Limit)]
    public void OrderBuilder_Build_ShouldCorrectlyMapFields(OrderSide side, OrderType type)
    {
        // Arrange
        var request = new TradeExecutionRequest
        {
            SignalId = Guid.NewGuid(),
            RiskEvaluationId = Guid.NewGuid(),
            Symbol = "BTCUSDT",
            Side = side,
            OrderType = type,
            Quantity = 0.5m,
            Price = 60000m,
            RiskDecision = RiskDecisionStatus.Approved
        };

        // Act
        var order = _builder.Build(request);

        // Assert
        order.Should().NotBeNull();
        order.Symbol.Should().Be("BTCUSDT");
        order.Side.Should().Be(side);
        order.Type.Should().Be(type);
        order.Quantity.Should().Be(0.5m);
        order.Price.Should().Be(60000m);
        order.SignalId.Should().Be(request.SignalId);
        order.RiskEvaluationId.Should().Be(request.RiskEvaluationId);
        order.ClientOrderId.Should().StartWith("BOT-");
    }

    #endregion

    #region Symbol Normalization Tests

    [Theory]
    [InlineData("btc/usdt", "BTCUSDT")]
    [InlineData("BTC-USDT", "BTCUSDT")]
    [InlineData("btc usdt", "BTCUSDT")]
    [InlineData("BTCUSDT", "BTCUSDT")]
    [InlineData("   sol/usdt   ", "SOLUSDT")]
    public void SymbolNormalizer_Normalize_ShouldProduceCanonicalForm(string input, string expected)
    {
        // Act
        var result = SymbolNormalizer.Normalize(input);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void OrderBuilder_ShouldNormalizeSymbol()
    {
        // Arrange
        var request = new TradeExecutionRequest { Symbol = "btc/usdt" };

        // Act
        var order = _builder.Build(request);

        // Assert
        order.Symbol.Should().Be("BTCUSDT");
    }

    #endregion

    #region Quantity Validation Tests

    [Fact]
    public void OrderValidator_Quantity_Positive_ShouldBeValid()
    {
        // Arrange
        var request = CreateBaseRequest();
        var order = _builder.Build(request);
        var rules = new InstrumentRules { Symbol = "BTCUSDT", MinQuantity = 0.01m, QuantityStep = 0.01m, MinNotional = 1m };

        // Act
        var result = _validator.Validate(request, order, rules);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.1)]
    public void OrderValidator_Quantity_ZeroOrNegative_ShouldBeInvalid(decimal qty)
    {
        // Arrange
        var request = CreateBaseRequest();
        request.Quantity = qty;
        var order = _builder.Build(request);
        var rules = new InstrumentRules { Symbol = "BTCUSDT", MinQuantity = 0.01m, QuantityStep = 0.01m, MinNotional = 1m };

        // Act
        var result = _validator.Validate(request, order, rules);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationCodes.Should().Contain("INVALID_QUANTITY");
    }

    [Fact]
    public void OrderValidator_Quantity_BelowMinimum_ShouldBeInvalid()
    {
        // Arrange
        var request = CreateBaseRequest();
        request.Quantity = 0.005m; // rules require 0.01
        var order = _builder.Build(request);
        var rules = new InstrumentRules { Symbol = "BTCUSDT", MinQuantity = 0.01m, QuantityStep = 0.001m, MinNotional = 1m };

        // Act
        var result = _validator.Validate(request, order, rules);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationCodes.Should().Contain("QUANTITY_BELOW_MINIMUM");
    }

    [Fact]
    public void OrderValidator_Quantity_AboveMaximum_ShouldBeInvalid()
    {
        // Arrange
        var request = CreateBaseRequest();
        request.Quantity = 200m; // rules max is 100
        var order = _builder.Build(request);
        var rules = new InstrumentRules { Symbol = "BTCUSDT", MinQuantity = 0.01m, MaxQuantity = 100m, QuantityStep = 0.001m, MinNotional = 1m };

        // Act
        var result = _validator.Validate(request, order, rules);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationCodes.Should().Contain("QUANTITY_ABOVE_MAXIMUM");
    }

    [Fact]
    public void OrderValidator_QuantityStep_InvalidStep_ShouldBeInvalid()
    {
        // Arrange
        var request = CreateBaseRequest();
        request.Quantity = 0.015m; // step is 0.01
        var order = _builder.Build(request);
        var rules = new InstrumentRules { Symbol = "BTCUSDT", MinQuantity = 0.01m, QuantityStep = 0.01m, MinNotional = 1m };

        // Act
        var result = _validator.Validate(request, order, rules);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationCodes.Should().Contain("INVALID_QUANTITY_STEP");
    }

    [Fact]
    public void OrderValidator_QuantityStep_ExactStep_ShouldBeValid()
    {
        // Arrange
        var request = CreateBaseRequest();
        request.Quantity = 0.02m; // step is 0.01
        var order = _builder.Build(request);
        var rules = new InstrumentRules { Symbol = "BTCUSDT", MinQuantity = 0.01m, QuantityStep = 0.01m, MinNotional = 1m };

        // Act
        var result = _validator.Validate(request, order, rules);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region Price Validation Tests

    [Fact]
    public void OrderValidator_Price_ValidLimitPrice_ShouldBeValid()
    {
        // Arrange
        var request = CreateBaseRequest();
        request.OrderType = OrderType.Limit;
        request.Price = 60000m;
        var order = _builder.Build(request);
        var rules = new InstrumentRules { Symbol = "BTCUSDT", TickSize = 0.10m, MinQuantity = 0.01m, QuantityStep = 0.01m, MinNotional = 1m };

        // Act
        var result = _validator.Validate(request, order, rules);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void OrderValidator_Price_LimitPriceZeroOrNegative_ShouldBeInvalid(decimal price)
    {
        // Arrange
        var request = CreateBaseRequest();
        request.OrderType = OrderType.Limit;
        request.Price = price;
        var order = _builder.Build(request);
        var rules = new InstrumentRules { Symbol = "BTCUSDT", TickSize = 0.10m, MinQuantity = 0.01m, QuantityStep = 0.01m, MinNotional = 1m };

        // Act
        var result = _validator.Validate(request, order, rules);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationCodes.Should().Contain("INVALID_LIMIT_PRICE");
    }

    [Fact]
    public void OrderValidator_Price_InvalidTickSize_ShouldBeInvalid()
    {
        // Arrange
        var request = CreateBaseRequest();
        request.OrderType = OrderType.Limit;
        request.Price = 60000.15m; // tick is 0.10
        var order = _builder.Build(request);
        var rules = new InstrumentRules { Symbol = "BTCUSDT", TickSize = 0.10m, MinQuantity = 0.01m, QuantityStep = 0.01m, MinNotional = 1m };

        // Act
        var result = _validator.Validate(request, order, rules);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationCodes.Should().Contain("INVALID_PRICE_TICK");
    }

    [Fact]
    public void OrderValidator_Price_ExactTickSize_ShouldBeValid()
    {
        // Arrange
        var request = CreateBaseRequest();
        request.OrderType = OrderType.Limit;
        request.Price = 60000.30m; // tick is 0.10
        var order = _builder.Build(request);
        var rules = new InstrumentRules { Symbol = "BTCUSDT", TickSize = 0.10m, MinQuantity = 0.01m, QuantityStep = 0.01m, MinNotional = 1m };

        // Act
        var result = _validator.Validate(request, order, rules);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void OrderValidator_Price_BelowMinimumTick_ShouldBeInvalid()
    {
        // Arrange
        var request = CreateBaseRequest();
        request.OrderType = OrderType.Limit;
        request.Price = 0.05m; // tick is 0.10
        var order = _builder.Build(request);
        var rules = new InstrumentRules { Symbol = "BTCUSDT", TickSize = 0.10m, MinQuantity = 0.01m, QuantityStep = 0.01m, MinNotional = 1m };

        // Act
        var result = _validator.Validate(request, order, rules);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationCodes.Should().Contain("PRICE_BELOW_MINIMUM");
    }

    #endregion

    #region Order Type Tests

    [Fact]
    public void OrderValidator_Type_Market_ShouldNotRequirePrice()
    {
        // Arrange
        var request = CreateBaseRequest();
        request.OrderType = OrderType.Market;
        request.Price = 0; // market order doesn't require limit price
        var order = _builder.Build(request);
        var rules = new InstrumentRules { Symbol = "BTCUSDT", TickSize = 0.10m, MinQuantity = 0.01m, QuantityStep = 0.01m, MinNotional = 1m };

        // Act
        var result = _validator.Validate(request, order, rules);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region Notional Validation Tests

    [Fact]
    public void OrderValidator_Notional_AboveMinimum_ShouldBeValid()
    {
        // Arrange
        var request = CreateBaseRequest();
        request.OrderType = OrderType.Limit;
        request.Quantity = 0.1m;
        request.Price = 100m; // Notional = 10m (MinNotional = 5.0m)
        var order = _builder.Build(request);
        var rules = new InstrumentRules { Symbol = "BTCUSDT", TickSize = 0.10m, MinQuantity = 0.01m, QuantityStep = 0.01m, MinNotional = 5.0m };

        // Act
        var result = _validator.Validate(request, order, rules);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void OrderValidator_Notional_BelowMinimum_ShouldBeInvalid()
    {
        // Arrange
        var request = CreateBaseRequest();
        request.OrderType = OrderType.Limit;
        request.Quantity = 0.01m;
        request.Price = 100m; // Notional = 1.0m (MinNotional = 5.0m)
        var order = _builder.Build(request);
        var rules = new InstrumentRules { Symbol = "BTCUSDT", TickSize = 0.10m, MinQuantity = 0.01m, QuantityStep = 0.01m, MinNotional = 5.0m };

        // Act
        var result = _validator.Validate(request, order, rules);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ValidationCodes.Should().Contain("NOTIONAL_BELOW_MINIMUM");
    }

    [Fact]
    public void OrderValidator_Notional_ExactMinimum_ShouldBeValid()
    {
        // Arrange
        var request = CreateBaseRequest();
        request.OrderType = OrderType.Limit;
        request.Quantity = 0.05m;
        request.Price = 100m; // Notional = 5.0m (MinNotional = 5.0m)
        var order = _builder.Build(request);
        var rules = new InstrumentRules { Symbol = "BTCUSDT", TickSize = 0.10m, MinQuantity = 0.01m, QuantityStep = 0.01m, MinNotional = 5.0m };

        // Act
        var result = _validator.Validate(request, order, rules);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region Risk Approval Tests

    [Theory]
    [InlineData(RiskDecisionStatus.Approved, true)]
    [InlineData(RiskDecisionStatus.Rejected, false)]
    [InlineData(RiskDecisionStatus.NeedsManualReview, false)]
    [InlineData(RiskDecisionStatus.NeedsReview, false)]
    public void OrderValidator_RiskApproval_Checks(RiskDecisionStatus decision, bool expectedValid)
    {
        // Arrange
        var request = CreateBaseRequest();
        request.RiskDecision = decision;
        var order = _builder.Build(request);
        var rules = new InstrumentRules { Symbol = "BTCUSDT", MinQuantity = 0.01m, QuantityStep = 0.01m, MinNotional = 1m };

        // Act
        var result = _validator.Validate(request, order, rules);

        // Assert
        result.IsValid.Should().Be(expectedValid);
        if (!expectedValid)
        {
            result.ValidationCodes.Should().Contain("RISK_APPROVAL_REQUIRED");
        }
    }

    #endregion

    #region Fail-Closed Tests

    [Fact]
    public void OrderValidator_MissingInstrumentRules_ShouldFailClosed()
    {
        // Arrange
        var request = CreateBaseRequest();
        var order = _builder.Build(request);

        // Act
        var result = _validator.Validate(request, order, null); // passing null rules

        // Assert
        result.IsValid.Should().BeFalse();
        result.Severity.Should().Be(ValidationSeverity.Critical);
        result.ValidationCodes.Should().Contain("MISSING_INSTRUMENT_RULES");
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task TradingExecutionService_ValidOrder_ShouldCallGateway_AndReturnSuccess()
    {
        // Arrange
        var request = CreateBaseRequest();
        request.Symbol = "BTCUSDT";
        request.Quantity = 0.05m;
        request.Price = 60000m;
        request.OrderType = OrderType.Limit;

        var mockGatewayResult = new OrderResult
        {
            Success = true,
            ExchangeOrderId = "12345678",
            Status = OrderStatus.New,
            ErrorMessage = "Order created successfully."
        };

        _gatewayMock
            .Setup(x => x.CreateOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockGatewayResult);

        var service = new TradingExecutionService(_validator, _builder, _gatewayMock.Object, _instrumentRules, _loggerMock.Object);

        // Act
        var result = await service.ExecuteAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Status.Should().Be(OrderStatus.New);
        result.ExchangeOrderId.Should().Be("12345678");

        // Verify that the exchange gateway was invoked
        _gatewayMock.Verify(x => x.CreateOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()), Times.Once());
    }

    [Fact]
    public async Task TradingExecutionService_InvalidOrder_ShouldNotCallGateway_AndReturnValidationFailed()
    {
        // Arrange
        var request = CreateBaseRequest();
        request.Symbol = "BTCUSDT";
        request.Quantity = 0.0001m; // invalid, below BTC min of 0.001
        request.OrderType = OrderType.Limit;
        request.Price = 60000m;

        var service = new TradingExecutionService(_validator, _builder, _gatewayMock.Object, _instrumentRules, _loggerMock.Object);

        // Act
        var result = await service.ExecuteAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Status.Should().Be(OrderStatus.ValidationFailed);
        result.Message.Should().Contain("below minimum");

        // Verify that the exchange gateway was NOT invoked
        _gatewayMock.Verify(x => x.CreateOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()), Times.Never());
    }

    #endregion

    private TradeExecutionRequest CreateBaseRequest()
    {
        return new TradeExecutionRequest
        {
            SignalId = Guid.NewGuid(),
            RiskEvaluationId = Guid.NewGuid(),
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            OrderType = OrderType.Market,
            Quantity = 0.10m,
            Price = 0m,
            RiskDecision = RiskDecisionStatus.Approved
        };
    }
}
