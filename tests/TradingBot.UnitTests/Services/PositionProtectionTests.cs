using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Repositories;
using TradingBot.Application.Services;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;
using Xunit;

namespace TradingBot.UnitTests.Services;

public class PositionProtectionTests
{
    private readonly Mock<IPositionRepository> _positionRepoMock;
    private readonly Mock<IExchangeTradingGateway> _exchangeGatewayMock;
    private readonly Mock<IExchangeInstrumentRules> _instrumentRulesMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<ILogger<StopLossManager>> _slLoggerMock;
    private readonly Mock<ILogger<TakeProfitManager>> _tpLoggerMock;
    private readonly Mock<ILogger<PartialCloseManager>> _pcLoggerMock;

    public PositionProtectionTests()
    {
        _positionRepoMock = new Mock<IPositionRepository>();
        _exchangeGatewayMock = new Mock<IExchangeTradingGateway>();
        _instrumentRulesMock = new Mock<IExchangeInstrumentRules>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _slLoggerMock = new Mock<ILogger<StopLossManager>>();
        _tpLoggerMock = new Mock<ILogger<TakeProfitManager>>();
        _pcLoggerMock = new Mock<ILogger<PartialCloseManager>>();
    }

    #region Stop Loss Tests

    [Fact]
    public async Task StopLoss_Long_ValidSL_ShouldSucceed()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 60000m, 1m);
        _positionRepoMock.Setup(r => r.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _exchangeGatewayMock.Setup(g => g.SetTradingStopAsync(position.Symbol, position.Side, 59000m, It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderResult { Success = true, Status = OrderStatus.Filled });

        var slManager = new StopLossManager(_positionRepoMock.Object, _exchangeGatewayMock.Object, _instrumentRulesMock.Object, _unitOfWorkMock.Object, _slLoggerMock.Object);

        // Act
        var result = await slManager.UpdateStopLossAsync(position.Id, 59000m);

        // Assert
        result.Should().BeTrue();
        position.StopLoss.Should().Be(59000m);
        position.StopLossHistories.Should().HaveCount(1);
        position.StopLossHistories.First().NewPrice.Should().Be(59000m);
        position.Events.Should().Contain(e => e.EventType == "StopLossCreated");
    }

    [Fact]
    public async Task StopLoss_Long_InvalidSL_ShouldThrowDomainException()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 60000m, 1m);
        _positionRepoMock.Setup(r => r.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        var slManager = new StopLossManager(_positionRepoMock.Object, _exchangeGatewayMock.Object, _instrumentRulesMock.Object, _unitOfWorkMock.Object, _slLoggerMock.Object);

        // Act & Assert
        Func<Task> act = async () => await slManager.UpdateStopLossAsync(position.Id, 61000m);
        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*must be less than EntryPrice*");
    }

    [Fact]
    public async Task StopLoss_Short_ValidSL_ShouldSucceed()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Sell, 60000m, 1m);
        _positionRepoMock.Setup(r => r.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _exchangeGatewayMock.Setup(g => g.SetTradingStopAsync(position.Symbol, position.Side, 61000m, It.IsAny<decimal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderResult { Success = true, Status = OrderStatus.Filled });

        var slManager = new StopLossManager(_positionRepoMock.Object, _exchangeGatewayMock.Object, _instrumentRulesMock.Object, _unitOfWorkMock.Object, _slLoggerMock.Object);

        // Act
        var result = await slManager.UpdateStopLossAsync(position.Id, 61000m);

        // Assert
        result.Should().BeTrue();
        position.StopLoss.Should().Be(61000m);
    }

    [Fact]
    public async Task StopLoss_Short_InvalidSL_ShouldThrowDomainException()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Sell, 60000m, 1m);
        _positionRepoMock.Setup(r => r.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        var slManager = new StopLossManager(_positionRepoMock.Object, _exchangeGatewayMock.Object, _instrumentRulesMock.Object, _unitOfWorkMock.Object, _slLoggerMock.Object);

        // Act & Assert
        Func<Task> act = async () => await slManager.UpdateStopLossAsync(position.Id, 59000m);
        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*must be greater than EntryPrice*");
    }

    [Fact]
    public async Task StopLoss_InvalidPrecision_ShouldThrowDomainException()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 60000m, 1m);
        _positionRepoMock.Setup(r => r.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _instrumentRulesMock.Setup(i => i.GetInstrumentRules(position.Symbol))
            .Returns(new InstrumentRules { Symbol = "BTCUSDT", PricePrecision = 2, TickSize = 0.5m });

        var slManager = new StopLossManager(_positionRepoMock.Object, _exchangeGatewayMock.Object, _instrumentRulesMock.Object, _unitOfWorkMock.Object, _slLoggerMock.Object);

        // Act & Assert
        Func<Task> act = async () => await slManager.UpdateStopLossAsync(position.Id, 59000.123m);
        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*exceeds the allowed price precision*");
    }

    #endregion

    #region Take Profit Tests

    [Fact]
    public async Task TakeProfit_Long_ValidTargets_ShouldSucceed()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 60000m, 0.01m);
        _positionRepoMock.Setup(r => r.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _exchangeGatewayMock.Setup(g => g.CreateOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderResult { Success = true, ExchangeOrderId = "EX-123", Status = OrderStatus.New });

        var tpManager = new TakeProfitManager(_positionRepoMock.Object, _exchangeGatewayMock.Object, _instrumentRulesMock.Object, _unitOfWorkMock.Object, _tpLoggerMock.Object);

        var targetsInput = new List<(decimal Price, decimal Percentage)>
        {
            (62000m, 50m),
            (63000m, 50m)
        };

        // Act
        var result = await tpManager.CreateTakeProfitTargetsAsync(position.Id, targetsInput);

        // Assert
        result.Should().HaveCount(2);
        position.Targets.Should().HaveCount(2);
        result[0].TargetNumber.Should().Be(1);
        result[0].Price.Should().Be(62000m);
        result[0].Quantity.Should().Be(0.005m);
        result[0].Status.Should().Be("Active");
        result[0].ExchangeOrderId.Should().Be("EX-123");
    }

    [Fact]
    public async Task TakeProfit_Long_InvalidPrice_ShouldThrowDomainException()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 60000m, 0.01m);
        _positionRepoMock.Setup(r => r.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        var tpManager = new TakeProfitManager(_positionRepoMock.Object, _exchangeGatewayMock.Object, _instrumentRulesMock.Object, _unitOfWorkMock.Object, _tpLoggerMock.Object);

        var targetsInput = new List<(decimal Price, decimal Percentage)>
        {
            (59000m, 100m)
        };

        // Act & Assert
        Func<Task> act = async () => await tpManager.CreateTakeProfitTargetsAsync(position.Id, targetsInput);
        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*must be greater than EntryPrice*");
    }

    [Fact]
    public async Task TakeProfit_Short_InvalidPrice_ShouldThrowDomainException()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Sell, 60000m, 0.01m);
        _positionRepoMock.Setup(r => r.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        var tpManager = new TakeProfitManager(_positionRepoMock.Object, _exchangeGatewayMock.Object, _instrumentRulesMock.Object, _unitOfWorkMock.Object, _tpLoggerMock.Object);

        var targetsInput = new List<(decimal Price, decimal Percentage)>
        {
            (61000m, 100m)
        };

        // Act & Assert
        Func<Task> act = async () => await tpManager.CreateTakeProfitTargetsAsync(position.Id, targetsInput);
        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*must be less than EntryPrice*");
    }

    [Fact]
    public async Task TakeProfit_PercentageExceeded_ShouldThrowDomainException()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 60000m, 0.01m);
        _positionRepoMock.Setup(r => r.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        var tpManager = new TakeProfitManager(_positionRepoMock.Object, _exchangeGatewayMock.Object, _instrumentRulesMock.Object, _unitOfWorkMock.Object, _tpLoggerMock.Object);

        var targetsInput = new List<(decimal Price, decimal Percentage)>
        {
            (62000m, 60m),
            (63000m, 50m)
        };

        // Act & Assert
        Func<Task> act = async () => await tpManager.CreateTakeProfitTargetsAsync(position.Id, targetsInput);
        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*cannot exceed 100%*");
    }

    [Fact]
    public async Task TakeProfit_InvalidOrdering_ShouldThrowDomainException()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 60000m, 0.01m);
        _positionRepoMock.Setup(r => r.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        var tpManager = new TakeProfitManager(_positionRepoMock.Object, _exchangeGatewayMock.Object, _instrumentRulesMock.Object, _unitOfWorkMock.Object, _tpLoggerMock.Object);

        // Act & Assert
        var targetsInput = new List<(decimal Price, decimal Percentage)>
        {
            (63000m, 50m),
            (62000m, 50m) // sorted: 62000 (50%), 63000 (50%). Price-sorted order is correct but if we pass duplicate or inconsistent prices it throws. Let's pass duplicate to see.
        };

        var targetsDuplicateInput = new List<(decimal Price, decimal Percentage)>
        {
            (62000m, 50m),
            (62000m, 50m)
        };

        Func<Task> act = async () => await tpManager.CreateTakeProfitTargetsAsync(position.Id, targetsDuplicateInput);
        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*must be strictly ascending*");
    }

    #endregion

    #region Partial Close Tests

    [Fact]
    public async Task PartialClose_Valid_ShouldSucceed()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 60000m, 0.01m);
        _positionRepoMock.Setup(r => r.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _exchangeGatewayMock.Setup(g => g.CreateOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderResult { Success = true, Status = OrderStatus.Filled, ExecutedQuantity = 0.005m, ExecutedPrice = 61000m });

        var pcManager = new PartialCloseManager(_positionRepoMock.Object, _exchangeGatewayMock.Object, _instrumentRulesMock.Object, _unitOfWorkMock.Object, _pcLoggerMock.Object);

        // Act
        var result = await pcManager.ExecutePartialCloseAsync(position.Id, 0.005m);

        // Assert
        result.Should().BeTrue();
        position.RemainingQuantity.Should().Be(0.005m);
        position.Status.Should().Be(PositionStatus.PartiallyClosed);
        position.Events.Should().Contain(e => e.EventType == "PositionPartiallyClosed");
    }

    [Fact]
    public async Task PartialClose_FullClose_ShouldSucceed()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 60000m, 0.01m);
        _positionRepoMock.Setup(r => r.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        _exchangeGatewayMock.Setup(g => g.CreateOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderResult { Success = true, Status = OrderStatus.Filled, ExecutedQuantity = 0.01m, ExecutedPrice = 61000m });

        var pcManager = new PartialCloseManager(_positionRepoMock.Object, _exchangeGatewayMock.Object, _instrumentRulesMock.Object, _unitOfWorkMock.Object, _pcLoggerMock.Object);

        // Act
        var result = await pcManager.ExecutePartialCloseAsync(position.Id, 0.01m);

        // Assert
        result.Should().BeTrue();
        position.RemainingQuantity.Should().Be(0m);
        position.Status.Should().Be(PositionStatus.Closed);
        position.Events.Should().Contain(e => e.EventType == "PositionClosed");
    }

    [Fact]
    public async Task PartialClose_OverClose_ShouldThrowDomainException()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 60000m, 0.01m);
        _positionRepoMock.Setup(r => r.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        var pcManager = new PartialCloseManager(_positionRepoMock.Object, _exchangeGatewayMock.Object, _instrumentRulesMock.Object, _unitOfWorkMock.Object, _pcLoggerMock.Object);

        // Act & Assert
        Func<Task> act = async () => await pcManager.ExecutePartialCloseAsync(position.Id, 0.02m);
        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*Cannot close more than the remaining position quantity*");
    }

    [Fact]
    public async Task TakeProfitHit_DuplicateProtection_ShouldBeIdempotent()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 60000m, 0.01m);
        var target = new PositionTarget(position.Id, 1, 62000m, 0.005m, 50m);
        target.SetExchangeOrderId("EX-TP-999");
        position.Targets.Add(target);

        _positionRepoMock.Setup(r => r.GetOpenPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Position> { position });

        _positionRepoMock.Setup(r => r.GetByIdAsync(position.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        var pcManager = new PartialCloseManager(_positionRepoMock.Object, _exchangeGatewayMock.Object, _instrumentRulesMock.Object, _unitOfWorkMock.Object, _pcLoggerMock.Object);

        // Act - First trigger
        var firstResult = await pcManager.ProcessTakeProfitHitAsync("EX-TP-999", 0.005m, 62000m);

        // Assert first
        firstResult.Should().BeTrue();
        target.Status.Should().Be("Executed");
        position.RemainingQuantity.Should().Be(0.005m);
        position.Status.Should().Be(PositionStatus.PartiallyClosed);

        // Reset target quantity and state checking for second trigger
        // Act - Second trigger (duplicate event)
        var secondResult = await pcManager.ProcessTakeProfitHitAsync("EX-TP-999", 0.005m, 62000m);

        // Assert second
        secondResult.Should().BeTrue();
        position.RemainingQuantity.Should().Be(0.005m); // No extra closing happened!
    }

    #endregion
}
