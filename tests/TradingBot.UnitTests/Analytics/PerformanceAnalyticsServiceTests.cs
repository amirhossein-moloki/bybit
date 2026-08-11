using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using TradingBot.Application.Analytics.DTOs;
using TradingBot.Application.Analytics.Interfaces;
using TradingBot.Application.Analytics.Services;
using TradingBot.Domain.Enums;
using Xunit;

namespace TradingBot.UnitTests.Analytics;

public class PerformanceAnalyticsServiceTests
{
    private readonly DrawdownCalculator _drawdownCalculator;
    private readonly StreakCalculator _streakCalculator;
    private readonly PnLCalculator _pnlCalculator;

    public PerformanceAnalyticsServiceTests()
    {
        _drawdownCalculator = new DrawdownCalculator();
        _streakCalculator = new StreakCalculator();
        _pnlCalculator = new PnLCalculator();
    }

    private static AnalyticsTradeDto CreateTradeDto(
        decimal netPnL,
        OrderSide side,
        DateTime openedAt,
        DateTime closedAt,
        string symbol = "BTCUSDT")
    {
        return new AnalyticsTradeDto(
            Id: Guid.NewGuid(),
            NetPnL: netPnL,
            ProfitLoss: netPnL + 1m, // just some dummy value
            Fee: 1m,
            OpenedAt: openedAt,
            ClosedAt: closedAt,
            Symbol: symbol,
            Side: side
        );
    }

    private class FakeQueryService : IPerformanceAnalyticsQueryService
    {
        public List<AnalyticsTradeDto> Trades { get; set; } = new();

        public Task<IReadOnlyList<AnalyticsTradeDto>> GetCompletedTradesAsync(
            GetAnalyticsQuery query,
            CancellationToken cancellationToken = default)
        {
            if (query.StartDate.HasValue && query.EndDate.HasValue && query.StartDate.Value > query.EndDate.Value)
            {
                throw new ArgumentException("The 'StartDate' must be less than or equal to the 'EndDate'.");
            }

            IEnumerable<AnalyticsTradeDto> filtered = Trades;

            if (!string.IsNullOrWhiteSpace(query.Symbol))
            {
                filtered = filtered.Where(t => t.Symbol == query.Symbol);
            }

            if (query.StartDate.HasValue)
            {
                filtered = filtered.Where(t => t.ClosedAt >= query.StartDate.Value);
            }

            if (query.EndDate.HasValue)
            {
                filtered = filtered.Where(t => t.ClosedAt <= query.EndDate.Value);
            }

            return Task.FromResult<IReadOnlyList<AnalyticsTradeDto>>(filtered.ToList());
        }
    }

    // --- Win/Loss & Performance Tests (REQ-11-301, REQ-11-302, REQ-11-303) ---

    [Fact]
    public async Task PerformanceMetrics_WithEmptyTrades_ShouldReturnZeroes()
    {
        // Arrange
        var fakeQuery = new FakeQueryService();
        var service = new PerformanceAnalyticsService(fakeQuery, _drawdownCalculator, _streakCalculator, _pnlCalculator);

        // Act
        var result = await service.GetPerformanceMetricsAsync(new GetAnalyticsQuery());

        // Assert
        result.TotalTrades.Should().Be(0);
        result.WinningTrades.Should().Be(0);
        result.LosingTrades.Should().Be(0);
        result.BreakevenTrades.Should().Be(0);
        result.WinRate.Should().Be(0m);
        result.LossRate.Should().Be(0m);
        result.NetPnL.Should().Be(0m);
        result.ProfitFactor.Should().Be(0m);
    }

    [Fact]
    public async Task PerformanceMetrics_WithAllWinningTrades_ShouldCalculateCorrectly()
    {
        // Arrange
        var fakeQuery = new FakeQueryService();
        var now = DateTime.UtcNow;
        fakeQuery.Trades.AddRange(new[]
        {
            CreateTradeDto(100m, OrderSide.Buy, now.AddMinutes(-10), now),
            CreateTradeDto(200m, OrderSide.Buy, now.AddMinutes(-20), now.AddMinutes(-5))
        });
        var service = new PerformanceAnalyticsService(fakeQuery, _drawdownCalculator, _streakCalculator, _pnlCalculator);

        // Act
        var result = await service.GetPerformanceMetricsAsync(new GetAnalyticsQuery());

        // Assert
        result.TotalTrades.Should().Be(2);
        result.WinningTrades.Should().Be(2);
        result.LosingTrades.Should().Be(0);
        result.BreakevenTrades.Should().Be(0);
        result.WinRate.Should().Be(100m);
        result.LossRate.Should().Be(0m);
        result.AverageWin.Should().Be(150m);
        result.LargestWin.Should().Be(200m);
        result.LargestLoss.Should().Be(0m);
        result.NetPnL.Should().Be(300m);
        result.ProfitFactor.Should().Be(0m); // zero loss
    }

    [Fact]
    public async Task PerformanceMetrics_WithAllLosingTrades_ShouldCalculateCorrectly()
    {
        // Arrange
        var fakeQuery = new FakeQueryService();
        var now = DateTime.UtcNow;
        fakeQuery.Trades.AddRange(new[]
        {
            CreateTradeDto(-50m, OrderSide.Buy, now.AddMinutes(-10), now),
            CreateTradeDto(-150m, OrderSide.Buy, now.AddMinutes(-20), now.AddMinutes(-5))
        });
        var service = new PerformanceAnalyticsService(fakeQuery, _drawdownCalculator, _streakCalculator, _pnlCalculator);

        // Act
        var result = await service.GetPerformanceMetricsAsync(new GetAnalyticsQuery());

        // Assert
        result.TotalTrades.Should().Be(2);
        result.WinningTrades.Should().Be(0);
        result.LosingTrades.Should().Be(2);
        result.WinRate.Should().Be(0m);
        result.LossRate.Should().Be(100m);
        result.AverageLoss.Should().Be(100m); // represented as positive magnitude
        result.LargestLoss.Should().Be(150m); // represented as positive magnitude
        result.NetPnL.Should().Be(-200m);
        result.ProfitFactor.Should().Be(0m); // zero profit
    }

    [Fact]
    public async Task PerformanceMetrics_WithMixedAndBreakevenTrades_ShouldCalculateCorrectly()
    {
        // Arrange
        var fakeQuery = new FakeQueryService();
        var now = DateTime.UtcNow;
        fakeQuery.Trades.AddRange(new[]
        {
            CreateTradeDto(150m, OrderSide.Buy, now.AddMinutes(-30), now.AddMinutes(-25)), // Win
            CreateTradeDto(-50m, OrderSide.Sell, now.AddMinutes(-20), now.AddMinutes(-15)), // Loss
            CreateTradeDto(0m, OrderSide.Buy, now.AddMinutes(-10), now) // Breakeven
        });
        var service = new PerformanceAnalyticsService(fakeQuery, _drawdownCalculator, _streakCalculator, _pnlCalculator);

        // Act
        var result = await service.GetPerformanceMetricsAsync(new GetAnalyticsQuery());

        // Assert
        result.TotalTrades.Should().Be(3);
        result.WinningTrades.Should().Be(1);
        result.LosingTrades.Should().Be(1);
        result.BreakevenTrades.Should().Be(1);
        result.WinRate.Should().BeApproximately(100m / 3m, 0.0001m);
        result.LossRate.Should().BeApproximately(100m / 3m, 0.0001m);
        result.NetPnL.Should().Be(100m);
        result.ProfitFactor.Should().Be(3m); // 150 / 50 = 3
    }

    // --- Drawdown Tests (REQ-11-304) ---

    [Fact]
    public void DrawdownCalculator_WithIncreasingEquity_ShouldHaveZeroDrawdown()
    {
        // Arrange
        var netPnLs = new List<decimal> { 100m, 200m, 300m };

        // Act
        var result = _drawdownCalculator.Calculate(netPnLs, 1000m);

        // Assert
        result.PeakEquity.Should().Be(1600m);
        result.CurrentEquity.Should().Be(1600m);
        result.Drawdown.Should().Be(0m);
        result.MaximumDrawdown.Should().Be(0m);
        result.MaximumDrawdownPercentage.Should().Be(0m);
    }

    [Fact]
    public void DrawdownCalculator_WithSingleDrawdown_ShouldCalculateCorrectly()
    {
        // Arrange
        // Equity starts at 1000.
        // T1: +50 => 1050 (peak = 1050, drawdown = 0)
        // T2: +50 => 1100 (peak = 1100, drawdown = 0)
        // T3: -100 => 1000 (peak = 1100, drawdown = 100)
        var netPnLs = new List<decimal> { 50m, 50m, -100m };

        // Act
        var result = _drawdownCalculator.Calculate(netPnLs, 1000m);

        // Assert
        result.PeakEquity.Should().Be(1100m);
        result.CurrentEquity.Should().Be(1000m);
        result.Drawdown.Should().Be(100m);
        result.MaximumDrawdown.Should().Be(100m);
        result.MaximumDrawdownPercentage.Should().Be(100m / 1100m * 100m);
    }

    [Fact]
    public void DrawdownCalculator_WithMultipleDrawdowns_ShouldTrackMaxDrawdown()
    {
        // Arrange
        // Equity starts at 10000.
        // T1: -1000 => 9000 (peak = 10000, maxDD = 1000, DD% = 10%)
        // T2: +2000 => 11000 (peak = 11000, maxDD = 1000)
        // T3: -3000 => 8000 (peak = 11000, DD = 3000, maxDD = 3000, DD% = 3000 / 11000 = 27.27%)
        var netPnLs = new List<decimal> { -1000m, 2000m, -3000m };

        // Act
        var result = _drawdownCalculator.Calculate(netPnLs, 10000m);

        // Assert
        result.PeakEquity.Should().Be(11000m);
        result.CurrentEquity.Should().Be(8000m);
        result.MaximumDrawdown.Should().Be(3000m);
        result.MaximumDrawdownPercentage.Should().Be(3000m / 11000m * 100m);
    }

    // --- Streak Tests (REQ-11-305) ---

    [Fact]
    public void StreakCalculator_WithContinuousWins_ShouldCalculateCorrectWinStreaks()
    {
        // Arrange
        var netPnLs = new List<decimal> { 10m, 20m, 30m };

        // Act
        var result = _streakCalculator.Calculate(netPnLs);

        // Assert
        result.CurrentWinStreak.Should().Be(3);
        result.CurrentLossStreak.Should().Be(0);
        result.MaximumWinStreak.Should().Be(3);
        result.MaximumLossStreak.Should().Be(0);
    }

    [Fact]
    public void StreakCalculator_WithContinuousLosses_ShouldCalculateCorrectLossStreaks()
    {
        // Arrange
        var netPnLs = new List<decimal> { -10m, -20m, -30m };

        // Act
        var result = _streakCalculator.Calculate(netPnLs);

        // Assert
        result.CurrentWinStreak.Should().Be(0);
        result.CurrentLossStreak.Should().Be(3);
        result.MaximumWinStreak.Should().Be(0);
        result.MaximumLossStreak.Should().Be(3);
    }

    [Fact]
    public void StreakCalculator_WithBreakevenReset_ShouldResetStreaks()
    {
        // Arrange
        // W, W, B, L, W
        var netPnLs = new List<decimal> { 10m, 20m, 0m, -10m, 10m };

        // Act
        var result = _streakCalculator.Calculate(netPnLs);

        // Assert
        // After B, current win resets to 0. After L, current win is 0, loss is 1. After final W, win is 1, loss is 0.
        result.CurrentWinStreak.Should().Be(1);
        result.CurrentLossStreak.Should().Be(0);
        result.MaximumWinStreak.Should().Be(2);
        result.MaximumLossStreak.Should().Be(1);
    }

    // --- Duration Tests (REQ-11-306) ---

    [Fact]
    public async Task DurationAnalytics_WithValidAndInvalidTrades_ShouldCalculateCorrectly()
    {
        // Arrange
        var fakeQuery = new FakeQueryService();
        var now = DateTime.UtcNow;

        fakeQuery.Trades.AddRange(new[]
        {
            // Valid trade: 30 minutes duration, winning
            CreateTradeDto(50m, OrderSide.Buy, now.AddMinutes(-30), now),
            // Valid trade: 10 minutes duration, losing
            CreateTradeDto(-20m, OrderSide.Buy, now.AddMinutes(-15), now.AddMinutes(-5)),
            // Invalid duration: negative (ClosedAt < OpenedAt) -> ignored
            CreateTradeDto(10m, OrderSide.Buy, now, now.AddMinutes(-5)),
            // Invalid duration: missing timestamp -> ignored
            new AnalyticsTradeDto(Guid.NewGuid(), 10m, 10m, 0m, null, now, "BTCUSDT", OrderSide.Buy)
        });

        var service = new PerformanceAnalyticsService(fakeQuery, _drawdownCalculator, _streakCalculator, _pnlCalculator);

        // Act
        var result = await service.GetDurationMetricsAsync(new GetAnalyticsQuery());

        // Assert
        result.AverageDuration.Should().Be(TimeSpan.FromMinutes(20)); // (30 + 10) / 2
        result.ShortestDuration.Should().Be(TimeSpan.FromMinutes(10));
        result.LongestDuration.Should().Be(TimeSpan.FromMinutes(30));
        result.AverageWinningDuration.Should().Be(TimeSpan.FromMinutes(30));
        result.AverageLosingDuration.Should().Be(TimeSpan.FromMinutes(10));
    }

    // --- Side Performance Tests (REQ-11-307) ---

    [Fact]
    public async Task SidePerformance_WithOnlyLongTrades_ShouldSetShortToZero()
    {
        // Arrange
        var fakeQuery = new FakeQueryService();
        var now = DateTime.UtcNow;

        fakeQuery.Trades.AddRange(new[]
        {
            CreateTradeDto(100m, OrderSide.Buy, now.AddMinutes(-10), now),
            CreateTradeDto(-20m, OrderSide.Buy, now.AddMinutes(-20), now.AddMinutes(-15))
        });

        var service = new PerformanceAnalyticsService(fakeQuery, _drawdownCalculator, _streakCalculator, _pnlCalculator);

        // Act
        var result = await service.GetLongShortPerformanceAsync(new GetAnalyticsQuery());

        // Assert
        result.Long.Trades.Should().Be(2);
        result.Long.Wins.Should().Be(1);
        result.Long.Losses.Should().Be(1);
        result.Long.WinRate.Should().Be(50m);
        result.Long.TotalPnL.Should().Be(80m);
        result.Long.AveragePnL.Should().Be(40m);

        result.Short.Trades.Should().Be(0);
        result.Short.Wins.Should().Be(0);
        result.Short.Losses.Should().Be(0);
        result.Short.WinRate.Should().Be(0m);
        result.Short.TotalPnL.Should().Be(0m);
        result.Short.AveragePnL.Should().Be(0m);
    }

    [Fact]
    public async Task SidePerformance_WithOnlyShortTrades_ShouldSetLongToZero()
    {
        // Arrange
        var fakeQuery = new FakeQueryService();
        var now = DateTime.UtcNow;

        fakeQuery.Trades.AddRange(new[]
        {
            CreateTradeDto(150m, OrderSide.Sell, now.AddMinutes(-10), now),
            CreateTradeDto(-50m, OrderSide.Sell, now.AddMinutes(-20), now.AddMinutes(-15))
        });

        var service = new PerformanceAnalyticsService(fakeQuery, _drawdownCalculator, _streakCalculator, _pnlCalculator);

        // Act
        var result = await service.GetLongShortPerformanceAsync(new GetAnalyticsQuery());

        // Assert
        result.Short.Trades.Should().Be(2);
        result.Short.Wins.Should().Be(1);
        result.Short.Losses.Should().Be(1);
        result.Short.WinRate.Should().Be(50m);
        result.Short.TotalPnL.Should().Be(100m);
        result.Short.AveragePnL.Should().Be(50m);

        result.Long.Trades.Should().Be(0);
        result.Long.Wins.Should().Be(0);
        result.Long.Losses.Should().Be(0);
        result.Long.WinRate.Should().Be(0m);
        result.Long.TotalPnL.Should().Be(0m);
        result.Long.AveragePnL.Should().Be(0m);
    }

    [Fact]
    public async Task SidePerformance_WithMixedLongAndShortTrades_ShouldCalculateBoth()
    {
        // Arrange
        var fakeQuery = new FakeQueryService();
        var now = DateTime.UtcNow;

        fakeQuery.Trades.AddRange(new[]
        {
            CreateTradeDto(100m, OrderSide.Buy, now.AddMinutes(-10), now), // Long win
            CreateTradeDto(-20m, OrderSide.Buy, now.AddMinutes(-20), now.AddMinutes(-15)), // Long loss
            CreateTradeDto(150m, OrderSide.Sell, now.AddMinutes(-10), now), // Short win
            CreateTradeDto(-50m, OrderSide.Sell, now.AddMinutes(-20), now.AddMinutes(-15)) // Short loss
        });

        var service = new PerformanceAnalyticsService(fakeQuery, _drawdownCalculator, _streakCalculator, _pnlCalculator);

        // Act
        var result = await service.GetLongShortPerformanceAsync(new GetAnalyticsQuery());

        // Assert
        result.Long.Trades.Should().Be(2);
        result.Long.TotalPnL.Should().Be(80m);

        result.Short.Trades.Should().Be(2);
        result.Short.TotalPnL.Should().Be(100m);
    }
}
