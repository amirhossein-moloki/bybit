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

public class PositionManagementTests
{
    private readonly Mock<IPositionRepository> _positionRepoMock;
    private readonly Mock<ITradeRepository> _tradeRepoMock;
    private readonly Mock<IExchangeTradingGateway> _exchangeGatewayMock;
    private readonly Mock<IExchangeInstrumentRules> _instrumentRulesMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IStopLossManager> _stopLossManagerMock;

    public PositionManagementTests()
    {
        _positionRepoMock = new Mock<IPositionRepository>();
        _tradeRepoMock = new Mock<ITradeRepository>();
        _exchangeGatewayMock = new Mock<IExchangeTradingGateway>();
        _instrumentRulesMock = new Mock<IExchangeInstrumentRules>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _stopLossManagerMock = new Mock<IStopLossManager>();
    }

    #region PnL Calculator Tests

    [Fact]
    public void PnLCalculator_LongPnL_ShouldBeCorrect()
    {
        // Arrange
        var calculator = new PnLCalculator();

        // Act
        var gross = calculator.CalculateGrossPnL(OrderSide.Buy, 50000m, 55000m, 2.0m);
        var net = calculator.CalculateNetPnL(gross, 10m, 5m);

        // Assert
        gross.Should().Be(10000m); // (55000 - 50000) * 2 = 10000
        net.Should().Be(9985m);    // 10000 - 10 - 5 = 9985
    }

    [Fact]
    public void PnLCalculator_ShortPnL_ShouldBeCorrect()
    {
        // Arrange
        var calculator = new PnLCalculator();

        // Act
        var gross = calculator.CalculateGrossPnL(OrderSide.Sell, 50000m, 45000m, 2.0m);
        var net = calculator.CalculateNetPnL(gross, 10m, 5m);

        // Assert
        gross.Should().Be(10000m); // (50000 - 45000) * 2 = 10000
        net.Should().Be(9985m);    // 10000 - 10 - 5 = 9985
    }

    #endregion

    #region Break-Even Tests

    [Fact]
    public async Task BreakEven_Long_TriggerPercentage_ShouldSucceed()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 50000m, 1.0m, stopLoss: 45000m);
        _positionRepoMock.Setup(r => r.GetByIdAsync(position.Id, It.IsAny<CancellationToken>())).ReturnsAsync(position);

        _stopLossManagerMock.Setup(s => s.UpdateStopLossAsync(position.Id, It.IsAny<decimal?>(), "Break-Even", "System", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var settings = new BreakEvenSettings
        {
            Enabled = true,
            TriggerType = BreakEvenTriggerType.Percentage,
            TriggerValue = 2.0m, // 2% above entry = 51000
            Offset = 100m       // 100 USDT offset = 50100
        };

        var manager = new BreakEvenManager(_positionRepoMock.Object, _stopLossManagerMock.Object, _unitOfWorkMock.Object, Mock.Of<ILogger<BreakEvenManager>>());

        // Act
        var result = await manager.ExecuteBreakEvenCheckAsync(position.Id, 51100m, settings);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task BreakEven_Short_TriggerPrice_ShouldSucceed()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Sell, 50000m, 1.0m, stopLoss: 55000m);
        _positionRepoMock.Setup(r => r.GetByIdAsync(position.Id, It.IsAny<CancellationToken>())).ReturnsAsync(position);

        _stopLossManagerMock.Setup(s => s.UpdateStopLossAsync(position.Id, It.IsAny<decimal?>(), "Break-Even", "System", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var settings = new BreakEvenSettings
        {
            Enabled = true,
            TriggerType = BreakEvenTriggerType.Price,
            TriggerValue = 49000m,
            Offset = 100m // Offset below entry = 49900
        };

        var manager = new BreakEvenManager(_positionRepoMock.Object, _stopLossManagerMock.Object, _unitOfWorkMock.Object, Mock.Of<ILogger<BreakEvenManager>>());

        // Act
        var result = await manager.ExecuteBreakEvenCheckAsync(position.Id, 48900m, settings);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task BreakEven_RMultiple_Trigger_ShouldSucceed()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 50000m, 1.0m, stopLoss: 48000m); // R = 2000
        _positionRepoMock.Setup(r => r.GetByIdAsync(position.Id, It.IsAny<CancellationToken>())).ReturnsAsync(position);

        _stopLossManagerMock.Setup(s => s.UpdateStopLossAsync(position.Id, It.IsAny<decimal?>(), "Break-Even", "System", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var settings = new BreakEvenSettings
        {
            Enabled = true,
            TriggerType = BreakEvenTriggerType.RMultiple,
            TriggerValue = 1.0m, // 1R above entry = 52000
            Offset = 0m
        };

        var manager = new BreakEvenManager(_positionRepoMock.Object, _stopLossManagerMock.Object, _unitOfWorkMock.Object, Mock.Of<ILogger<BreakEvenManager>>());

        // Act
        var result = await manager.ExecuteBreakEvenCheckAsync(position.Id, 52100m, settings);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task BreakEven_ShouldSkip_WhenAlreadyActivated()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 50000m, 1.0m, stopLoss: 45000m);
        position.Events.Add(new PositionEvent(position.Id, "BreakEvenActivated", "{}"));
        _positionRepoMock.Setup(r => r.GetByIdAsync(position.Id, It.IsAny<CancellationToken>())).ReturnsAsync(position);

        var settings = new BreakEvenSettings
        {
            Enabled = true,
            TriggerType = BreakEvenTriggerType.Percentage,
            TriggerValue = 1.0m
        };

        var manager = new BreakEvenManager(_positionRepoMock.Object, _stopLossManagerMock.Object, _unitOfWorkMock.Object, Mock.Of<ILogger<BreakEvenManager>>());

        // Act
        var result = await manager.ExecuteBreakEvenCheckAsync(position.Id, 52000m, settings);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Trailing Stop Tests

    [Fact]
    public async Task TrailingStop_Long_Distance_ShouldSucceed_AndObeyStep()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 50000m, 1.0m, stopLoss: 49000m);
        _positionRepoMock.Setup(r => r.GetByIdAsync(position.Id, It.IsAny<CancellationToken>())).ReturnsAsync(position);

        _stopLossManagerMock.Setup(s => s.UpdateStopLossAsync(position.Id, It.IsAny<decimal?>(), "Trailing Stop", "System", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var settings = new TrailingStopSettings
        {
            Enabled = true,
            Distance = 1000m,
            Step = 200m // Improvement from 49000 to 51000 is 2000, which is >= 200 step
        };

        var manager = new TrailingStopManager(_positionRepoMock.Object, _stopLossManagerMock.Object, _unitOfWorkMock.Object, Mock.Of<ILogger<TrailingStopManager>>());

        // Act
        var result = await manager.ExecuteTrailingStopCheckAsync(position.Id, 52000m, settings); // DesiredSL = 52000 - 1000 = 51000

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task TrailingStop_Long_Percentage_ShouldSucceed()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 50000m, 1.0m, stopLoss: 49000m);
        _positionRepoMock.Setup(r => r.GetByIdAsync(position.Id, It.IsAny<CancellationToken>())).ReturnsAsync(position);

        _stopLossManagerMock.Setup(s => s.UpdateStopLossAsync(position.Id, It.IsAny<decimal?>(), "Trailing Stop", "System", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var settings = new TrailingStopSettings
        {
            Enabled = true,
            Percentage = 1.5m, // 1.5% distance = 51150 at price 51928 (approx. 778 USDT distance)
            Step = 0m
        };

        var manager = new TrailingStopManager(_positionRepoMock.Object, _stopLossManagerMock.Object, _unitOfWorkMock.Object, Mock.Of<ILogger<TrailingStopManager>>());

        // Act
        var result = await manager.ExecuteTrailingStopCheckAsync(position.Id, 51928.9m, settings); // DesiredSL = 51928.9 * 0.985 = 51150

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task TrailingStop_ShouldNotUpdate_WhenStepNotSatisfied()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 50000m, 1.0m, stopLoss: 49000m);
        _positionRepoMock.Setup(r => r.GetByIdAsync(position.Id, It.IsAny<CancellationToken>())).ReturnsAsync(position);

        var settings = new TrailingStopSettings
        {
            Enabled = true,
            Distance = 1000m,
            Step = 500m // Step is 500, but improvement from 49000 to 49100 is only 100!
        };

        var manager = new TrailingStopManager(_positionRepoMock.Object, _stopLossManagerMock.Object, _unitOfWorkMock.Object, Mock.Of<ILogger<TrailingStopManager>>());

        // Act
        var result = await manager.ExecuteTrailingStopCheckAsync(position.Id, 50100m, settings); // DesiredSL = 50100 - 1000 = 49100

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Position Close Manager Tests

    [Fact]
    public async Task ClosePosition_ValidClose_ShouldSucceedAndSettleTrade()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 50000m, 1.0m);
        position.UpdatePrice(51000m); // Floating PnL is 1000
        position.PartialClose(0.5m, 51000m, 5m); // Lock in some realized PnL (500) and fee (5)

        _positionRepoMock.Setup(r => r.GetByIdAsync(position.Id, It.IsAny<CancellationToken>())).ReturnsAsync(position);

        _exchangeGatewayMock.Setup(g => g.CreateOrderAsync(It.IsAny<OrderRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrderResult { Success = true, ExecutedQuantity = 0.5m, ExecutedPrice = 52000m, Status = OrderStatus.Filled });

        Trade? savedTrade = null;
        _tradeRepoMock.Setup(t => t.SaveAsync(It.IsAny<Trade>(), It.IsAny<CancellationToken>()))
            .Callback<Trade, CancellationToken>((tr, ct) => savedTrade = tr)
            .Returns(Task.CompletedTask);

        var pnlCalc = new PnLCalculator();
        var closeManager = new PositionCloseManager(
            _positionRepoMock.Object,
            _tradeRepoMock.Object,
            _exchangeGatewayMock.Object,
            pnlCalc,
            _unitOfWorkMock.Object,
            Mock.Of<ILogger<PositionCloseManager>>()
        );

        // Act
        var result = await closeManager.ClosePositionAsync(position.Id, CloseReason.TakeProfit, exitPrice: 52000m);

        // Assert
        result.Should().BeTrue();
        position.Status.Should().Be(PositionStatus.Closed);
        position.RemainingQuantity.Should().Be(0);
        position.Events.Should().Contain(e => e.EventType == "PositionClosed");

        savedTrade.Should().NotBeNull();
        savedTrade!.PositionId.Should().Be(position.Id);
        savedTrade.GrossPnL.Should().Be(1500m); // 500 from first partial + (52000 - 50000) * 0.5 = 1000, Total = 1500
        savedTrade.TradingFee.Should().Be(5m);   // Fee from partial close
        savedTrade.NetPnL.Should().Be(1495m);    // 1500 - 5 = 1495
        savedTrade.CloseReason.Should().Be(CloseReason.TakeProfit);
    }

    [Fact]
    public async Task ClosePosition_Liquidation_ShouldSettleAsLiquidation()
    {
        // Arrange
        var position = new Position(Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, 50000m, 1.0m);
        _positionRepoMock.Setup(r => r.GetByIdAsync(position.Id, It.IsAny<CancellationToken>())).ReturnsAsync(position);

        _tradeRepoMock.Setup(t => t.SaveAsync(It.IsAny<Trade>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var closeManager = new PositionCloseManager(
            _positionRepoMock.Object,
            _tradeRepoMock.Object,
            _exchangeGatewayMock.Object,
            new PnLCalculator(),
            _unitOfWorkMock.Object,
            Mock.Of<ILogger<PositionCloseManager>>()
        );

        // Act
        var result = await closeManager.ClosePositionAsync(position.Id, CloseReason.Liquidation, exitPrice: 40000m);

        // Assert
        result.Should().BeTrue();
        position.Status.Should().Be(PositionStatus.Liquidated);
        position.Events.Should().Contain(e => e.EventType == "PositionLiquidated");
    }

    #endregion
}
