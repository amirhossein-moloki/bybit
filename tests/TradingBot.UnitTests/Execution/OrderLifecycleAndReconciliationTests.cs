using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TradingBot.Application.Repositories;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Application.Trading.Execution.Services;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;
using TradingBot.Domain.ValueObjects;
using Xunit;
using Symbol = TradingBot.Domain.ValueObjects.Symbol;

namespace TradingBot.UnitTests.Execution;

public class OrderLifecycleAndReconciliationTests
{
    [Fact]
    public void OrderStateMachine_ShouldEnforceValidTransitions_AndRejectInvalid()
    {
        var order = new Order("CLIENT-1", new Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.1m), new Money(50000m));

        order.Status.Should().Be(OrderStatus.Created);

        // Created -> Pending
        order.UpdateStatus(OrderStatus.Pending);
        order.Status.Should().Be(OrderStatus.Pending);

        // Pending -> Submitting
        order.UpdateStatus(OrderStatus.Submitting);
        order.Status.Should().Be(OrderStatus.Submitting);

        // Submitting -> Submitted
        order.UpdateStatus(OrderStatus.Submitted);
        order.Status.Should().Be(OrderStatus.Submitted);

        // Submitted -> New
        order.UpdateStatus(OrderStatus.New);
        order.Status.Should().Be(OrderStatus.New);

        // New -> PartiallyFilled
        order.UpdateStatus(OrderStatus.PartiallyFilled);
        order.Status.Should().Be(OrderStatus.PartiallyFilled);

        // PartiallyFilled -> Filled
        order.UpdateStatus(OrderStatus.Filled);
        order.Status.Should().Be(OrderStatus.Filled);

        // Filled is terminal. Filled -> Cancelled is invalid.
        Action act = () => order.UpdateStatus(OrderStatus.Cancelled);
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void RecordExecution_ShouldCalculateWeightedAveragePriceAndFillStatus()
    {
        var order = new Order("CLIENT-1", new Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1.0m), new Money(50000m));

        order.UpdateStatus(OrderStatus.Pending);
        order.UpdateStatus(OrderStatus.Submitting);
        order.UpdateStatus(OrderStatus.Submitted);
        order.UpdateStatus(OrderStatus.New);

        // Execution 1: 0.4 BTC @ 49,000
        order.RecordExecution(0.4m, 49000m);
        order.Status.Should().Be(OrderStatus.PartiallyFilled);
        order.ExecutedQuantity.Should().Be(0.4m);
        order.ExecutedPrice.Should().Be(49000m);

        // Execution 2: 0.6 BTC @ 51,000
        order.RecordExecution(0.6m, 51000m);
        order.Status.Should().Be(OrderStatus.Filled);
        order.ExecutedQuantity.Should().Be(1.0m);
        // Weighted average: (0.4 * 49000 + 0.6 * 51000) / 1.0 = 19600 + 30600 = 50200
        order.ExecutedPrice.Should().Be(50200m);
        order.FilledAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ReconciliationService_ShouldPreventStateDowngrade_AndLogConflict()
    {
        var mockOrderRepo = new Mock<IOrderRepository>();
        var mockEventRepo = new Mock<IOrderEventRepository>();
        var mockGateway = new Mock<IExchangeTradingGateway>();
        var mockUow = new Mock<IUnitOfWork>();
        var mockLogger = new Mock<ILogger<OrderReconciliationService>>();

        var order = new Order("CLIENT-1", new Symbol("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.1m), new Money(50000m));
        order.UpdateStatus(OrderStatus.Pending);
        order.UpdateStatus(OrderStatus.Submitting);
        order.UpdateStatus(OrderStatus.Submitted);
        order.UpdateStatus(OrderStatus.New);
        order.UpdateStatus(OrderStatus.Filled);

        // Set local state to Filled
        mockOrderRepo.Setup(x => x.GetPendingReconciliationOrdersAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { order });

        // Gateway returns New status (which is a downgrade!)
        mockGateway.Setup(x => x.GetOrderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderResult
            {
                Success = true,
                ExchangeOrderId = "EX-1",
                Status = OrderStatus.New,
                ExecutedQuantity = 0m
            });

        var reconciliationService = new OrderReconciliationService(
            mockOrderRepo.Object,
            mockEventRepo.Object,
            mockGateway.Object,
            mockUow.Object,
            mockLogger.Object);

        // Act
        await reconciliationService.ReconcileAsync();

        // Assert: Local status should still be Filled!
        order.Status.Should().Be(OrderStatus.Filled);
        mockEventRepo.Verify(x => x.AddAsync(It.Is<OrderEvent>(e => e.EventType == "OrderStateConflict"), It.IsAny<CancellationToken>()), Times.Once);
    }
}
