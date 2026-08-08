using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TradingBot.Application.Trading.Execution.Contracts;
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

    public TradingExecutionTests()
    {
        _loggerMock = new Mock<ILogger<TradingExecutionService>>();
        _validator = new OrderValidator();
        _builder = new OrderBuilder();
    }

    [Fact]
    public async Task ExecuteAsync_WithApprovedRisk_ShouldAllowExecution()
    {
        // Arrange
        var request = new TradeExecutionRequest
        {
            SignalId = Guid.NewGuid(),
            RiskEvaluationId = Guid.NewGuid(),
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            OrderType = OrderType.Market,
            Quantity = 0.05m,
            Price = 60000m,
            RiskDecision = RiskDecisionStatus.Approved
        };

        var gateway = new TestExchangeTradingGateway(simulateFailure: false);
        var service = new TradingExecutionService(_validator, _builder, gateway, _loggerMock.Object);

        // Act
        var result = await service.ExecuteAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.OrderId.Should().NotBeNull();
        result.ExchangeOrderId.Should().NotBeEmpty();
        result.Status.Should().Be(OrderStatus.Filled);
    }

    [Fact]
    public async Task ExecuteAsync_WithRejectedRisk_ShouldRejectExecution()
    {
        // Arrange
        var request = new TradeExecutionRequest
        {
            SignalId = Guid.NewGuid(),
            RiskEvaluationId = Guid.NewGuid(),
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            OrderType = OrderType.Market,
            Quantity = 0.05m,
            Price = 60000m,
            RiskDecision = RiskDecisionStatus.Rejected
        };

        var gateway = new TestExchangeTradingGateway(simulateFailure: false);
        var service = new TradingExecutionService(_validator, _builder, gateway, _loggerMock.Object);

        // Act
        var result = await service.ExecuteAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Status.Should().Be(OrderStatus.Rejected);
        result.Message.Should().Contain("Risk approval boundary violated");
    }

    [Fact]
    public async Task ExecuteAsync_WithNeedsManualReviewRisk_ShouldRejectExecution()
    {
        // Arrange
        var request = new TradeExecutionRequest
        {
            SignalId = Guid.NewGuid(),
            RiskEvaluationId = Guid.NewGuid(),
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            OrderType = OrderType.Market,
            Quantity = 0.05m,
            Price = 60000m,
            RiskDecision = RiskDecisionStatus.NeedsManualReview
        };

        var gateway = new TestExchangeTradingGateway(simulateFailure: false);
        var service = new TradingExecutionService(_validator, _builder, gateway, _loggerMock.Object);

        // Act
        var result = await service.ExecuteAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Status.Should().Be(OrderStatus.Rejected);
        result.Message.Should().Contain("Risk approval boundary violated");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.5)]
    public async Task ExecuteAsync_WithInvalidQuantity_ShouldRejectExecution(decimal invalidQuantity)
    {
        // Arrange
        var request = new TradeExecutionRequest
        {
            SignalId = Guid.NewGuid(),
            RiskEvaluationId = Guid.NewGuid(),
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            OrderType = OrderType.Market,
            Quantity = invalidQuantity,
            Price = 60000m,
            RiskDecision = RiskDecisionStatus.Approved
        };

        var gateway = new TestExchangeTradingGateway(simulateFailure: false);
        var service = new TradingExecutionService(_validator, _builder, gateway, _loggerMock.Object);

        // Act
        var result = await service.ExecuteAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Status.Should().Be(OrderStatus.Rejected);
        result.Message.Should().Contain("Quantity must be greater than zero");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task ExecuteAsync_WithLimitOrderAndInvalidPrice_ShouldRejectExecution(decimal invalidPrice)
    {
        // Arrange
        var request = new TradeExecutionRequest
        {
            SignalId = Guid.NewGuid(),
            RiskEvaluationId = Guid.NewGuid(),
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            OrderType = OrderType.Limit,
            Quantity = 0.01m,
            Price = invalidPrice,
            RiskDecision = RiskDecisionStatus.Approved
        };

        var gateway = new TestExchangeTradingGateway(simulateFailure: false);
        var service = new TradingExecutionService(_validator, _builder, gateway, _loggerMock.Object);

        // Act
        var result = await service.ExecuteAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Status.Should().Be(OrderStatus.Rejected);
        result.Message.Should().Contain("Limit Price must be greater than zero");
    }

    [Fact]
    public async Task ExecuteAsync_WithValidMarketOrder_ShouldPassValidationAndSucceed()
    {
        // Arrange
        var request = new TradeExecutionRequest
        {
            SignalId = Guid.NewGuid(),
            RiskEvaluationId = Guid.NewGuid(),
            Symbol = "ETHUSDT",
            Side = OrderSide.Sell,
            OrderType = OrderType.Market,
            Quantity = 1.25m,
            RiskDecision = RiskDecisionStatus.Approved
        };

        var gateway = new TestExchangeTradingGateway(simulateFailure: false);
        var service = new TradingExecutionService(_validator, _builder, gateway, _loggerMock.Object);

        // Act
        var result = await service.ExecuteAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.OrderId.Should().NotBeNull();
        result.ExchangeOrderId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithValidLimitOrder_ShouldPassValidationAndSucceed()
    {
        // Arrange
        var request = new TradeExecutionRequest
        {
            SignalId = Guid.NewGuid(),
            RiskEvaluationId = Guid.NewGuid(),
            Symbol = "ETHUSDT",
            Side = OrderSide.Buy,
            OrderType = OrderType.Limit,
            Quantity = 0.5m,
            Price = 3000m,
            RiskDecision = RiskDecisionStatus.Approved
        };

        var gateway = new TestExchangeTradingGateway(simulateFailure: false);
        var service = new TradingExecutionService(_validator, _builder, gateway, _loggerMock.Object);

        // Act
        var result = await service.ExecuteAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.OrderId.Should().NotBeNull();
        result.ExchangeOrderId.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WhenGatewayThrowsException_ShouldThrowExchangeGatewayException()
    {
        // Arrange
        var request = new TradeExecutionRequest
        {
            SignalId = Guid.NewGuid(),
            RiskEvaluationId = Guid.NewGuid(),
            Symbol = "ETHUSDT",
            Side = OrderSide.Buy,
            OrderType = OrderType.Market,
            Quantity = 0.5m,
            RiskDecision = RiskDecisionStatus.Approved
        };

        var gatewayMock = new Mock<IExchangeTradingGateway>();
        gatewayMock
            .Setup(g => g.CreateOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Network timeout"));

        var service = new TradingExecutionService(_validator, _builder, gatewayMock.Object, _loggerMock.Object);

        // Act
        Func<Task> act = async () => await service.ExecuteAsync(request);

        // Assert
        await act.Should().ThrowAsync<ExchangeGatewayException>()
            .WithMessage("Unexpected exchange gateway failure.");
    }
}
