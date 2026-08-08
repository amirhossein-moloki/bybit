using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingBot.Application.Repositories;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Events;
using TradingBot.Application.Trading.Execution.Exceptions;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Application.Trading.Execution.Services;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.RiskManagement.Enums;
using Xunit;

namespace TradingBot.UnitTests.Execution;

public class TradeExecutionOrchestratorTests
{
    private readonly Mock<IOrderValidator> _mockValidator;
    private readonly Mock<IOrderRepository> _mockOrderRepository;
    private readonly Mock<ITradeExecutionService> _mockExecutionService;
    private readonly Mock<IExecutionEventPublisher> _mockEventPublisher;
    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly TradeExecutionOrchestrator _orchestrator;

    public TradeExecutionOrchestratorTests()
    {
        _mockValidator = new Mock<IOrderValidator>();
        _mockOrderRepository = new Mock<IOrderRepository>();
        _mockExecutionService = new Mock<ITradeExecutionService>();
        _mockEventPublisher = new Mock<IExecutionEventPublisher>();
        _mockUnitOfWork = new Mock<IUnitOfWork>();

        _orchestrator = new TradeExecutionOrchestrator(
            _mockValidator.Object,
            _mockOrderRepository.Object,
            _mockExecutionService.Object,
            _mockEventPublisher.Object,
            _mockUnitOfWork.Object,
            NullLogger<TradeExecutionOrchestrator>.Instance
        );
    }

    [Fact]
    public async Task OrchestrateAsync_Success_ShouldPublishCorrectEventsAndReturnSuccess()
    {
        // Arrange
        var request = new TradeExecutionRequest
        {
            SignalId = Guid.NewGuid(),
            RiskEvaluationId = Guid.NewGuid(),
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            OrderType = OrderType.Limit,
            Quantity = 1.0m,
            Price = 60000m,
            RiskDecision = RiskDecisionStatus.Approved
        };

        _mockValidator.Setup(v => v.Validate(request));
        _mockOrderRepository.Setup(r => r.GetBySignalIdAsync(request.SignalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        var execResult = new ExecutionResult
        {
            Success = true,
            Status = OrderStatus.Filled,
            OrderId = Guid.NewGuid(),
            ExchangeOrderId = "EX-123",
            Message = "Executed successfully."
        };

        _mockExecutionService.Setup(s => s.ExecuteAsync(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(execResult);

        // Act
        var result = await _orchestrator.OrchestrateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Status.Should().Be(OrderStatus.Filled);
        result.OrderId.Should().Be(execResult.OrderId);
        result.ExchangeOrderId.Should().Be("EX-123");

        // Verify Events Published
        _mockEventPublisher.Verify(p => p.PublishAsync(It.IsAny<TradeExecutionStartedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockEventPublisher.Verify(p => p.PublishAsync(It.IsAny<OrderSubmissionStartedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockEventPublisher.Verify(p => p.PublishAsync(It.IsAny<OrderFilledEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockEventPublisher.Verify(p => p.PublishAsync(It.IsAny<TradeExecutionCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OrchestrateAsync_RiskRejected_ShouldRejectWithoutExecuting()
    {
        // Arrange
        var request = new TradeExecutionRequest
        {
            SignalId = Guid.NewGuid(),
            RiskEvaluationId = Guid.NewGuid(),
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            OrderType = OrderType.Limit,
            Quantity = 1.0m,
            Price = 60000m,
            RiskDecision = RiskDecisionStatus.Rejected
        };

        // Act
        var result = await _orchestrator.OrchestrateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Status.Should().Be(OrderStatus.ValidationFailed);
        result.FailureReason.Should().Contain("Risk approval boundary violated");

        // Verification
        _mockExecutionService.Verify(s => s.ExecuteAsync(It.IsAny<TradeExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockEventPublisher.Verify(p => p.PublishAsync(It.IsAny<OrderRejectedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockEventPublisher.Verify(p => p.PublishAsync(It.IsAny<TradeExecutionCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OrchestrateAsync_ValidationFailed_ShouldReturnFailure()
    {
        // Arrange
        var request = new TradeExecutionRequest
        {
            SignalId = Guid.NewGuid(),
            RiskEvaluationId = Guid.NewGuid(),
            Symbol = "BTC", // Invalid, too short
            Side = OrderSide.Buy,
            OrderType = OrderType.Limit,
            Quantity = 1.0m,
            Price = 60000m,
            RiskDecision = RiskDecisionStatus.Approved
        };

        _mockValidator.Setup(v => v.Validate(request))
            .Throws(new ExecutionValidationException("Symbol is too short."));

        // Act
        var result = await _orchestrator.OrchestrateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeFalse();
        result.Status.Should().Be(OrderStatus.ValidationFailed);
        result.FailureReason.Should().Be("Symbol is too short.");

        // Verification
        _mockExecutionService.Verify(s => s.ExecuteAsync(It.IsAny<TradeExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockEventPublisher.Verify(p => p.PublishAsync(It.IsAny<OrderRejectedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockEventPublisher.Verify(p => p.PublishAsync(It.IsAny<TradeExecutionCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OrchestrateAsync_DuplicateExecution_ShouldReturnExistingOrderDetails()
    {
        // Arrange
        var request = new TradeExecutionRequest
        {
            SignalId = Guid.NewGuid(),
            RiskEvaluationId = Guid.NewGuid(),
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            OrderType = OrderType.Limit,
            Quantity = 1.0m,
            Price = 60000m,
            RiskDecision = RiskDecisionStatus.Approved
        };

        _mockValidator.Setup(v => v.Validate(request));

        var existingOrder = new Order(
            Guid.NewGuid(),
            $"TB-{Guid.NewGuid():N}",
            new TradingBot.Domain.ValueObjects.Symbol("BTCUSDT"),
            OrderSide.Buy,
            OrderType.Limit,
            new TradingBot.Domain.ValueObjects.Quantity(1.0m),
            new TradingBot.Domain.ValueObjects.Money(60000m),
            request.SignalId
        );
        existingOrder.UpdateStatus(OrderStatus.Filled);

        _mockOrderRepository.Setup(r => r.GetBySignalIdAsync(request.SignalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingOrder);

        // Act
        var result = await _orchestrator.OrchestrateAsync(request);

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Status.Should().Be(OrderStatus.Filled);
        result.OrderId.Should().Be(existingOrder.Id);
        result.ExchangeOrderId.Should().Be("TEMP_EXCHANGE_ID");

        // Verification
        _mockExecutionService.Verify(s => s.ExecuteAsync(It.IsAny<TradeExecutionRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockEventPublisher.Verify(p => p.PublishAsync(It.IsAny<TradeExecutionCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
