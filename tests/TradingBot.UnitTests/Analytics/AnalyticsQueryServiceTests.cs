using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Analytics.DTOs;
using TradingBot.Application.Analytics.Interfaces;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Queries;
using Xunit;

namespace TradingBot.UnitTests.Analytics;

public class AnalyticsQueryServiceTests : IDisposable
{
    private readonly TradingDbContext _dbContext;
    private readonly IAnalyticsQueryService _queryService;

    public AnalyticsQueryServiceTests()
    {
        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new TradingDbContext(options);
        _queryService = new AnalyticsQueryService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    private Trade CreateCompletedTrade(
        decimal entryPrice,
        decimal exitPrice,
        decimal quantity,
        decimal profitLoss,
        decimal fee,
        DateTime closedAt,
        DateTime? openedAt = null,
        decimal? netPnL = null,
        string symbol = "BTCUSDT",
        SignalType side = SignalType.Buy)
    {
        var positionId = Guid.NewGuid();
        var opened = openedAt ?? closedAt.AddMinutes(-30);

        if (netPnL.HasValue)
        {
            var trade = new Trade(
                positionId: positionId,
                entryPrice: entryPrice,
                exitPrice: exitPrice,
                quantity: quantity,
                grossPnL: profitLoss,
                tradingFee: fee,
                fundingFee: 0m,
                netPnL: netPnL.Value,
                closeReason: CloseReason.TakeProfit,
                openedAt: opened,
                closedAt: closedAt
            );

            SetFieldOrProperty(trade, "Symbol", symbol);
            SetFieldOrProperty(trade, "Side", side);
            return trade;
        }
        else
        {
            var trade = new Trade(
                positionId: positionId,
                entryPrice: entryPrice,
                exitPrice: exitPrice,
                quantity: quantity,
                profitLoss: profitLoss,
                fee: fee,
                closedAt: closedAt
            );

            SetFieldOrProperty(trade, "OpenedAt", (DateTime?)opened);
            SetFieldOrProperty(trade, "Symbol", symbol);
            SetFieldOrProperty(trade, "Side", side);
            return trade;
        }
    }

    private void SetFieldOrProperty(object obj, string name, object? value)
    {
        var type = obj.GetType();
        var field = type.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        if (field != null)
        {
            field.SetValue(obj, value);
            return;
        }

        var prop = type.GetProperty(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        if (prop != null && prop.CanWrite)
        {
            prop.SetValue(obj, value);
        }
    }

    [Fact]
    public async Task GetTradeStatisticsAsync_WithEmptyDatabase_ShouldReturnSafeZeroesAndNoException()
    {
        // Arrange
        var query = new GetTradeStatisticsQuery();

        // Act
        var result = await _queryService.GetTradeStatisticsAsync(query, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.TotalTrades.Should().Be(0);
        result.WinningTrades.Should().Be(0);
        result.LosingTrades.Should().Be(0);
        result.BreakevenTrades.Should().Be(0);
        result.WinRate.Should().Be(0m);
        result.LossRate.Should().Be(0m);
        result.GrossProfit.Should().Be(0m);
        result.GrossLoss.Should().Be(0m);
        result.NetPnL.Should().Be(0m);
        result.AveragePnL.Should().Be(0m);
        result.AverageWin.Should().Be(0m);
        result.AverageLoss.Should().Be(0m);
        result.LargestWin.Should().Be(0m);
        result.LargestLoss.Should().Be(0m);
        result.ProfitFactor.Should().Be(0m);
        result.AverageDuration.Should().BeNull();
        result.ShortestDuration.Should().BeNull();
        result.LongestDuration.Should().BeNull();
        result.CurrentWinStreak.Should().Be(0);
        result.CurrentLossStreak.Should().Be(0);
        result.MaximumWinStreak.Should().Be(0);
        result.MaximumLossStreak.Should().Be(0);
    }

    [Fact]
    public async Task GetTradeStatisticsAsync_WithAllWinningTrades_ShouldCalculateCorrectly()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var t1 = CreateCompletedTrade(100m, 110m, 1m, 10m, 1m, baseTime.AddMinutes(-30), baseTime.AddMinutes(-60), netPnL: 9m); // Win: 9 (duration: 30m)
        var t2 = CreateCompletedTrade(100m, 115m, 1m, 15m, 2m, baseTime.AddMinutes(-10), baseTime.AddMinutes(-50), netPnL: 13m); // Win: 13 (duration: 40m)
        var t3 = CreateCompletedTrade(100m, 120m, 1m, 20m, 1m, baseTime, baseTime.AddMinutes(-20), netPnL: 19m); // Win: 19 (duration: 20m)

        _dbContext.Trades.AddRange(t1, t2, t3);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _queryService.GetTradeStatisticsAsync(new GetTradeStatisticsQuery(), CancellationToken.None);

        // Assert
        result.TotalTrades.Should().Be(3);
        result.WinningTrades.Should().Be(3);
        result.LosingTrades.Should().Be(0);
        result.BreakevenTrades.Should().Be(0);
        result.WinRate.Should().Be(100m);
        result.LossRate.Should().Be(0m);
        result.GrossProfit.Should().Be(41m); // 9 + 13 + 19
        result.GrossLoss.Should().Be(0m);
        result.NetPnL.Should().Be(41m);
        result.AveragePnL.Should().Be(41m / 3m);
        result.AverageWin.Should().Be(41m / 3m);
        result.AverageLoss.Should().Be(0m);
        result.LargestWin.Should().Be(19m);
        result.LargestLoss.Should().Be(0m);
        result.ProfitFactor.Should().Be(0m); // Division by 0 returns 0 as documented
        result.AverageDuration.Should().Be(TimeSpan.FromMinutes(30)); // (30+40+20)/3 = 30m
        result.ShortestDuration.Should().Be(TimeSpan.FromMinutes(20));
        result.LongestDuration.Should().Be(TimeSpan.FromMinutes(40));
        result.CurrentWinStreak.Should().Be(3);
        result.CurrentLossStreak.Should().Be(0);
        result.MaximumWinStreak.Should().Be(3);
        result.MaximumLossStreak.Should().Be(0);
    }

    [Fact]
    public async Task GetTradeStatisticsAsync_WithAllLosingTrades_ShouldCalculateCorrectly()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var t1 = CreateCompletedTrade(100m, 90m, 1m, -10m, 1m, baseTime.AddMinutes(-20), baseTime.AddMinutes(-30), netPnL: -11m); // Loss: -11 (duration: 10m)
        var t2 = CreateCompletedTrade(100m, 80m, 1m, -20m, 2m, baseTime, baseTime.AddMinutes(-50), netPnL: -22m); // Loss: -22 (duration: 50m)

        _dbContext.Trades.AddRange(t1, t2);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _queryService.GetTradeStatisticsAsync(new GetTradeStatisticsQuery(), CancellationToken.None);

        // Assert
        result.TotalTrades.Should().Be(2);
        result.WinningTrades.Should().Be(0);
        result.LosingTrades.Should().Be(2);
        result.BreakevenTrades.Should().Be(0);
        result.WinRate.Should().Be(0m);
        result.LossRate.Should().Be(100m);
        result.GrossProfit.Should().Be(0m);
        result.GrossLoss.Should().Be(33m); // Absolute magnitude: 11 + 22 = 33
        result.NetPnL.Should().Be(-33m);
        result.AveragePnL.Should().Be(-16.5m);
        result.AverageWin.Should().Be(0m);
        result.AverageLoss.Should().Be(16.5m); // Consistent positive magnitude representation
        result.LargestWin.Should().Be(0m);
        result.LargestLoss.Should().Be(22m); // Consistent positive magnitude representation
        result.ProfitFactor.Should().Be(0m);
        result.AverageDuration.Should().Be(TimeSpan.FromMinutes(30)); // (10+50)/2 = 30m
        result.ShortestDuration.Should().Be(TimeSpan.FromMinutes(10));
        result.LongestDuration.Should().Be(TimeSpan.FromMinutes(50));
        result.CurrentWinStreak.Should().Be(0);
        result.CurrentLossStreak.Should().Be(2);
        result.MaximumWinStreak.Should().Be(0);
        result.MaximumLossStreak.Should().Be(2);
    }

    [Fact]
    public async Task GetTradeStatisticsAsync_WithMixedResults_ShouldCalculateCorrectly()
    {
        // Arrange
        // We will seed: Win, Loss, Win, Breakeven, Loss, Win
        var baseTime = DateTime.UtcNow;
        var t1 = CreateCompletedTrade(100m, 110m, 1m, 10m, 1m, baseTime.AddMinutes(-50), netPnL: 9m); // Win: 9
        var t2 = CreateCompletedTrade(100m, 90m, 1m, -10m, 1m, baseTime.AddMinutes(-40), netPnL: -11m); // Loss: -11
        var t3 = CreateCompletedTrade(100m, 120m, 1m, 20m, 2m, baseTime.AddMinutes(-30), netPnL: 18m); // Win: 18
        var t4 = CreateCompletedTrade(100m, 100m, 1m, 0m, 0m, baseTime.AddMinutes(-20), netPnL: 0m); // Breakeven
        var t5 = CreateCompletedTrade(100m, 95m, 1m, -5m, 1m, baseTime.AddMinutes(-10), netPnL: -6m); // Loss: -6
        var t6 = CreateCompletedTrade(100m, 115m, 1m, 15m, 1m, baseTime, netPnL: 14m); // Win: 14

        _dbContext.Trades.AddRange(t1, t2, t3, t4, t5, t6);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _queryService.GetTradeStatisticsAsync(new GetTradeStatisticsQuery(), CancellationToken.None);

        // Assert
        result.TotalTrades.Should().Be(6);
        result.WinningTrades.Should().Be(3);
        result.LosingTrades.Should().Be(2);
        result.BreakevenTrades.Should().Be(1);
        result.WinRate.Should().Be(50m); // 3/6 = 50%
        result.LossRate.Should().Be(2/6.0m * 100m);

        result.GrossProfit.Should().Be(41m); // 9 + 18 + 14 = 41
        result.GrossLoss.Should().Be(17m); // 11 + 6 = 17
        result.NetPnL.Should().Be(24m); // 41 - 17 = 24

        result.AveragePnL.Should().Be(4m); // 24 / 6
        result.AverageWin.Should().Be(41m / 3m);
        result.AverageLoss.Should().Be(17m / 2m);
        result.LargestWin.Should().Be(18m);
        result.LargestLoss.Should().Be(11m);

        result.ProfitFactor.Should().Be(41m / 17m);

        // Streaks:
        // t1 (Win) -> currentWin = 1, maxWin = 1
        // t2 (Loss) -> currentLoss = 1, maxLoss = 1
        // t3 (Win) -> currentWin = 1, maxWin = 1
        // t4 (Breakeven) -> resets both to 0
        // t5 (Loss) -> currentLoss = 1, maxLoss = 1
        // t6 (Win) -> currentWin = 1, maxWin = 1
        result.CurrentWinStreak.Should().Be(1);
        result.CurrentLossStreak.Should().Be(0);
        result.MaximumWinStreak.Should().Be(1);
        result.MaximumLossStreak.Should().Be(1);
    }

    [Theory]
    [InlineData("W,W,W", 3, 0, 3, 0)]
    [InlineData("L,L,L", 0, 3, 0, 3)]
    [InlineData("W,L,W", 1, 0, 1, 1)]
    [InlineData("L,W,L", 0, 1, 1, 1)]
    [InlineData("W,W,L,W,W,W", 3, 0, 3, 1)]
    [InlineData("L,L,W,L", 0, 1, 1, 2)]
    [InlineData("W,B,W", 1, 0, 1, 0)]
    [InlineData("L,B,L", 0, 1, 0, 1)]
    public async Task GetTradeStatisticsAsync_StreakPatterns_ShouldAnalyzeCorrectly(
        string pattern,
        int expectedCurrWin,
        int expectedCurrLoss,
        int expectedMaxWin,
        int expectedMaxLoss)
    {
        // Clear trades first (since database is unique per test class instantiation, but we Dispose or use unique DB per run)
        // Actually, this test uses a single DbContext per test class instantiation, but we can clean the db for this theory or create a new db context.
        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        using var tempContext = new TradingDbContext(options);
        var tempService = new AnalyticsQueryService(tempContext);

        var baseTime = DateTime.UtcNow;
        var parts = pattern.Split(',');
        for (int i = 0; i < parts.Length; i++)
        {
            decimal netPnL = parts[i] switch
            {
                "W" => 10m,
                "L" => -10m,
                _ => 0m
            };

            var trade = CreateCompletedTrade(100m, 100m, 1m, netPnL, 0m, baseTime.AddMinutes(i), baseTime.AddMinutes(i - 10), netPnL: netPnL);
            tempContext.Trades.Add(trade);
        }
        await tempContext.SaveChangesAsync();

        // Act
        var result = await tempService.GetTradeStatisticsAsync(new GetTradeStatisticsQuery(), CancellationToken.None);

        // Assert
        result.CurrentWinStreak.Should().Be(expectedCurrWin, $"Pattern '{pattern}' CurrentWinStreak mismatch");
        result.CurrentLossStreak.Should().Be(expectedCurrLoss, $"Pattern '{pattern}' CurrentLossStreak mismatch");
        result.MaximumWinStreak.Should().Be(expectedMaxWin, $"Pattern '{pattern}' MaximumWinStreak mismatch");
        result.MaximumLossStreak.Should().Be(expectedMaxLoss, $"Pattern '{pattern}' MaximumLossStreak mismatch");
    }

    [Fact]
    public async Task GetTradeStatisticsAsync_WithZeroAndInvalidDurations_ShouldIgnoreInvalid()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        // Zero duration
        var tZero = CreateCompletedTrade(100m, 110m, 1m, 10m, 1m, baseTime, baseTime, netPnL: 9m);
        // Invalid duration (ClosedAt < OpenedAt)
        var tInvalid = CreateCompletedTrade(100m, 110m, 1m, 10m, 1m, baseTime, baseTime.AddMinutes(10), netPnL: 9m);
        // Missing opened timestamp (null)
        var tMissingOpened = CreateCompletedTrade(100m, 110m, 1m, 10m, 1m, baseTime, netPnL: 9m);
        SetFieldOrProperty(tMissingOpened, "OpenedAt", null);

        _dbContext.Trades.AddRange(tZero, tInvalid, tMissingOpened);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _queryService.GetTradeStatisticsAsync(new GetTradeStatisticsQuery(), CancellationToken.None);

        // Assert
        result.TotalTrades.Should().Be(3);
        // Only tZero has a valid duration (which is exactly 0 ticks)
        result.AverageDuration.Should().Be(TimeSpan.Zero);
        result.ShortestDuration.Should().Be(TimeSpan.Zero);
        result.LongestDuration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task GetTradeStatisticsAsync_DateRangeFiltering_ShouldOnlyIncludeMatching()
    {
        // Arrange: [From, To) is from 10:00 to 11:00
        var fromTime = new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var toTime = new DateTime(2025, 1, 1, 11, 0, 0, DateTimeKind.Utc);

        var tBefore = CreateCompletedTrade(100m, 110m, 1m, 10m, 1m, fromTime.AddMinutes(-5), netPnL: 9m); // 9:55
        var tAtFrom = CreateCompletedTrade(100m, 110m, 1m, 10m, 1m, fromTime, netPnL: 9m); // 10:00 (inside)
        var tInside = CreateCompletedTrade(100m, 110m, 1m, 10m, 1m, fromTime.AddMinutes(30), netPnL: 9m); // 10:30 (inside)
        var tAtTo = CreateCompletedTrade(100m, 110m, 1m, 10m, 1m, toTime, netPnL: 9m); // 11:00 (excluded because of [from, to))
        var tAfter = CreateCompletedTrade(100m, 110m, 1m, 10m, 1m, toTime.AddMinutes(5), netPnL: 9m); // 11:05 (excluded)

        _dbContext.Trades.AddRange(tBefore, tAtFrom, tInside, tAtTo, tAfter);
        await _dbContext.SaveChangesAsync();

        // Act
        var query = new GetTradeStatisticsQuery(From: fromTime, To: toTime);
        var result = await _queryService.GetTradeStatisticsAsync(query, CancellationToken.None);

        // Assert
        result.TotalTrades.Should().Be(2); // tAtFrom and tInside
    }

    [Fact]
    public async Task GetTradeStatisticsAsync_PrecisionTests_ShouldMaintainPreciseDecimalCalculations()
    {
        // Arrange: Use values that typically expose IEEE-754 floating point errors (e.g., 0.1, 0.2, 0.3)
        // Profit 0.1, 0.2, Loss 0.3
        var t1 = CreateCompletedTrade(100m, 100.1m, 1m, 0.1m, 0m, DateTime.UtcNow, netPnL: 0.1m);
        var t2 = CreateCompletedTrade(100m, 100.2m, 1m, 0.2m, 0m, DateTime.UtcNow, netPnL: 0.2m);
        var t3 = CreateCompletedTrade(100m, 99.7m, 1m, -0.3m, 0m, DateTime.UtcNow, netPnL: -0.3m);

        _dbContext.Trades.AddRange(t1, t2, t3);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _queryService.GetTradeStatisticsAsync(new GetTradeStatisticsQuery(), CancellationToken.None);

        // Assert
        // Summing 0.1m + 0.2m - 0.3m using precise decimal must be EXACTLY 0.0m
        result.NetPnL.Should().Be(0.0m);
        result.GrossProfit.Should().Be(0.3m);
        result.GrossLoss.Should().Be(0.3m);
        result.ProfitFactor.Should().Be(1.0m);
    }

    [Fact]
    public async Task GetTradeStatisticsAsync_DuplicateTradesInDatabase_ShouldPreserveIdentityAndRelyOnDatabaseGuarantees()
    {
        // Arrange: Seed two distinct trades with same parameters, they should be counted as 2 trades because they are distinct logical records (different IDs)
        var baseTime = DateTime.UtcNow;
        var t1 = CreateCompletedTrade(100m, 110m, 1m, 10m, 1m, baseTime, netPnL: 9m);
        var t2 = CreateCompletedTrade(100m, 110m, 1m, 10m, 1m, baseTime, netPnL: 9m);

        _dbContext.Trades.AddRange(t1, t2);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _queryService.GetTradeStatisticsAsync(new GetTradeStatisticsQuery(), CancellationToken.None);

        // Assert
        result.TotalTrades.Should().Be(2);
    }
}
