using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Application.Trading.Execution.Services;
using TradingBot.Domain.Enums;
using TradingBot.Domain.RiskManagement.Enums;
using Xunit;

namespace TradingBot.IntegrationTests.Services;

public class TradingExecutionIntegrationTests
{
    private readonly IServiceProvider _serviceProvider;

    public TradingExecutionIntegrationTests()
    {
        var services = new ServiceCollection();

        // Register core Stage 01 & Stage 02 services
        services.AddSingleton<IExchangeInstrumentRules, TestExchangeInstrumentRules>();
        services.AddScoped<IOrderValidator, OrderValidator>();
        services.AddScoped<IOrderBuilder, OrderBuilder>();
        services.AddScoped<IExchangeTradingGateway, TestExchangeTradingGateway>();
        services.AddScoped<ITradeExecutionService, TradingExecutionService>();

        // Register NullLogger for dependencies
        services.AddSingleton<ILogger<TradingExecutionService>>(NullLogger<TradingExecutionService>.Instance);

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task E2E_ApprovedTradeExecution_ShouldSucceedAndMapCorrectly()
    {
        // Arrange
        var executionService = _serviceProvider.GetRequiredService<ITradeExecutionService>();

        var request = new TradeExecutionRequest
        {
            SignalId = Guid.NewGuid(),
            RiskEvaluationId = Guid.NewGuid(),
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            OrderType = OrderType.Limit,
            Quantity = 0.25m,
            Price = 58500m,
            RiskDecision = RiskDecisionStatus.Approved
        };

        // Act
        var result = await executionService.ExecuteAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Status.Should().Be(OrderStatus.ReadyForExchange);
        result.Message.Should().Contain("ready for exchange");
    }

    [Fact]
    public async Task E2E_RejectedTradeExecution_ShouldEnforceBoundaryAndReject()
    {
        // Arrange
        var executionService = _serviceProvider.GetRequiredService<ITradeExecutionService>();

        var request = new TradeExecutionRequest
        {
            SignalId = Guid.NewGuid(),
            RiskEvaluationId = Guid.NewGuid(),
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            OrderType = OrderType.Limit,
            Quantity = 0.25m,
            Price = 58500m,
            RiskDecision = RiskDecisionStatus.Rejected
        };

        // Act
        var result = await executionService.ExecuteAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Status.Should().Be(OrderStatus.ValidationFailed);
        result.Message.Should().Contain("Validation failed");
        result.Message.Should().Contain("Risk approval boundary violated");
    }
}
