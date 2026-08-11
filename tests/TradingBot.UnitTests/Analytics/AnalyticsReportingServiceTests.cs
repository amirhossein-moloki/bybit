using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TradingBot.Application.Analytics.Configuration;
using TradingBot.Application.Analytics.DTOs;
using TradingBot.Application.Analytics.Interfaces;
using TradingBot.Application.Analytics.Services;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;
using Xunit;

namespace TradingBot.UnitTests.Analytics;

public class AnalyticsReportingServiceTests
{
    private readonly Mock<IAnalyticsReportingQueryService> _queryServiceMock;
    private readonly DrawdownCalculator _drawdownCalculator;
    private readonly StreakCalculator _streakCalculator;
    private readonly PnLCalculator _pnlCalculator;
    private readonly Mock<IReportScheduleRepository> _scheduleRepositoryMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly IMemoryCache _memoryCache;
    private readonly IOptions<AnalyticsReportOptions> _options;
    private readonly TradingDbContext _dbContext;
    private readonly Mock<ILogger<AnalyticsReportingService>> _loggerMock;

    public AnalyticsReportingServiceTests()
    {
        _queryServiceMock = new Mock<IAnalyticsReportingQueryService>();
        _drawdownCalculator = new DrawdownCalculator();
        _streakCalculator = new StreakCalculator();
        _pnlCalculator = new PnLCalculator();
        _scheduleRepositoryMock = new Mock<IReportScheduleRepository>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();

        var services = new ServiceCollection();
        services.AddMemoryCache();
        var serviceProvider = services.BuildServiceProvider();
        _memoryCache = serviceProvider.GetRequiredService<IMemoryCache>();

        _options = Options.Create(new AnalyticsReportOptions
        {
            EnableCaching = true,
            CacheTtlMinutes = 5,
            DefaultInitialBalance = 10000m
        });

        var dbOptions = new DbContextOptionsBuilder<TradingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new TradingDbContext(dbOptions);

        _loggerMock = new Mock<ILogger<AnalyticsReportingService>>();
    }

    private static ReportTradeDto CreateReportTrade(
        decimal netPnL,
        OrderSide side,
        DateTime openedAt,
        DateTime closedAt,
        CloseReason reason = CloseReason.TakeProfit)
    {
        return new ReportTradeDto(
            Id: Guid.NewGuid(),
            PositionId: Guid.NewGuid(),
            Symbol: "BTCUSDT",
            Side: side,
            EntryPrice: 50000m,
            ExitPrice: 51000m,
            Quantity: 1m,
            ProfitLoss: netPnL + 5m, // Gross PnL
            Fee: 5m,
            FundingFee: 0m,
            NetPnL: netPnL,
            CloseReason: reason,
            OpenedAt: openedAt,
            ClosedAt: closedAt
        );
    }

    [Fact]
    public async Task GenerateReportAsync_WithNoTrades_ShouldReturnEmptyReport()
    {
        // Arrange
        _queryServiceMock.Setup(x => x.GetReportTradesAsync(It.IsAny<ReportFilterDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ReportTradeDto>());

        var service = new AnalyticsReportingService(
            _queryServiceMock.Object, _drawdownCalculator, _streakCalculator, _pnlCalculator,
            _scheduleRepositoryMock.Object, _unitOfWorkMock.Object, _options, _loggerMock.Object);

        // Act
        var report = await service.GenerateReportAsync(new ReportFilterDto());

        // Assert
        report.Metrics.TotalTrades.Should().Be(0);
        report.InitialBalance.Should().Be(10000m);
        report.FinalBalance.Should().Be(10000m);
        report.EquityCurve.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateReportAsync_WithMixedTrades_ShouldCalculateMetricsCorrectly()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var trades = new List<ReportTradeDto>
        {
            CreateReportTrade(200m, OrderSide.Buy, baseTime.AddHours(-2), baseTime.AddHours(-1)), // Win
            CreateReportTrade(-100m, OrderSide.Sell, baseTime.AddMinutes(-30), baseTime, CloseReason.StopLoss) // Loss
        };

        _queryServiceMock.Setup(x => x.GetReportTradesAsync(It.IsAny<ReportFilterDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(trades);

        var service = new AnalyticsReportingService(
            _queryServiceMock.Object, _drawdownCalculator, _streakCalculator, _pnlCalculator,
            _scheduleRepositoryMock.Object, _unitOfWorkMock.Object, _options, _loggerMock.Object);

        // Act
        var report = await service.GenerateReportAsync(new ReportFilterDto());

        // Assert
        report.Metrics.TotalTrades.Should().Be(2);
        report.Metrics.WinningTrades.Should().Be(1);
        report.Metrics.LosingTrades.Should().Be(1);
        report.Metrics.NetPnL.Should().Be(100m);
        report.Metrics.ProfitFactor.Should().Be(2m); // 200 / 100 = 2
        report.Metrics.WinRate.Should().Be(50m);
        report.Metrics.LargestWin.Should().Be(200m);
        report.Metrics.LargestLoss.Should().Be(100m);
        report.FinalBalance.Should().Be(10100m);

        report.Drawdown.PeakEquity.Should().Be(10200m);
        report.Drawdown.CurrentEquity.Should().Be(10100m);

        report.Durations.AverageDuration.Should().Be(TimeSpan.FromMinutes(45)); // (60 + 30) / 2
        report.Durations.AverageWinningDuration.Should().Be(TimeSpan.FromHours(1));
        report.Durations.AverageLosingDuration.Should().Be(TimeSpan.FromMinutes(30));

        report.LongShort.Long.Trades.Should().Be(1);
        report.LongShort.Short.Trades.Should().Be(1);
    }

    [Fact]
    public async Task GetEquityCurveAsync_ShouldReturnStepByStepEquityPoints()
    {
        // Arrange
        var baseTime = DateTime.UtcNow;
        var trades = new List<ReportTradeDto>
        {
            CreateReportTrade(500m, OrderSide.Buy, baseTime.AddHours(-3), baseTime.AddHours(-2)), // 10500
            CreateReportTrade(-300m, OrderSide.Buy, baseTime.AddHours(-2), baseTime.AddHours(-1)), // 10200
            CreateReportTrade(1000m, OrderSide.Sell, baseTime.AddHours(-1), baseTime) // 11200
        };

        _queryServiceMock.Setup(x => x.GetReportTradesAsync(It.IsAny<ReportFilterDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(trades);

        var service = new AnalyticsReportingService(
            _queryServiceMock.Object, _drawdownCalculator, _streakCalculator, _pnlCalculator,
            _scheduleRepositoryMock.Object, _unitOfWorkMock.Object, _options, _loggerMock.Object);

        // Act
        var curve = await service.GetEquityCurveAsync(new ReportFilterDto());

        // Assert
        curve.Should().HaveCount(3);

        curve[0].TradeIndex.Should().Be(1);
        curve[0].Equity.Should().Be(10500m);
        curve[0].CumulativePnL.Should().Be(500m);
        curve[0].PeakEquity.Should().Be(10500m);
        curve[0].Drawdown.Should().Be(0m);

        curve[1].TradeIndex.Should().Be(2);
        curve[1].Equity.Should().Be(10200m);
        curve[1].CumulativePnL.Should().Be(200m);
        curve[1].PeakEquity.Should().Be(10500m);
        curve[1].Drawdown.Should().Be(300m);
        curve[1].DrawdownPercentage.Should().BeApproximately(300m / 10500m * 100m, 0.0001m);

        curve[2].TradeIndex.Should().Be(3);
        curve[2].Equity.Should().Be(11200m);
        curve[2].CumulativePnL.Should().Be(1200m);
        curve[2].PeakEquity.Should().Be(11200m);
        curve[2].Drawdown.Should().Be(0m);
    }

    [Fact]
    public async Task GetHistoricalAggregationAsync_Daily_ShouldGroupTradesByDate()
    {
        // Arrange
        var day1 = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2026, 8, 2, 14, 0, 0, DateTimeKind.Utc);

        var trades = new List<ReportTradeDto>
        {
            CreateReportTrade(150m, OrderSide.Buy, day1.AddMinutes(-30), day1),
            CreateReportTrade(-50m, OrderSide.Buy, day1.AddMinutes(-10), day1.AddMinutes(5)),
            CreateReportTrade(300m, OrderSide.Sell, day2.AddMinutes(-40), day2)
        };

        _queryServiceMock.Setup(x => x.GetReportTradesAsync(It.IsAny<ReportFilterDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(trades);

        var service = new AnalyticsReportingService(
            _queryServiceMock.Object, _drawdownCalculator, _streakCalculator, _pnlCalculator,
            _scheduleRepositoryMock.Object, _unitOfWorkMock.Object, _options, _loggerMock.Object);

        // Act
        var agg = await service.GetHistoricalAggregationAsync(new ReportFilterDto(), AggregationPeriod.Daily);

        // Assert
        agg.Should().HaveCount(2);

        agg[0].PeriodLabel.Should().Be("2026-08-01");
        agg[0].TotalTrades.Should().Be(2);
        agg[0].WinningTrades.Should().Be(1);
        agg[0].NetPnL.Should().Be(100m);
        agg[0].TotalFees.Should().Be(10m);

        agg[1].PeriodLabel.Should().Be("2026-08-02");
        agg[1].TotalTrades.Should().Be(1);
        agg[1].WinningTrades.Should().Be(1);
        agg[1].NetPnL.Should().Be(300m);
        agg[1].TotalFees.Should().Be(5m);
    }

    [Fact]
    public async Task ExportTradesToCsvAsync_ShouldGenerateFormattedCsvString()
    {
        // Arrange
        var baseTime = new DateTime(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);
        var tradeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var posId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var trade = new ReportTradeDto(
            Id: tradeId,
            PositionId: posId,
            Symbol: "BTCUSDT",
            Side: OrderSide.Buy,
            EntryPrice: 50000m,
            ExitPrice: 51000m,
            Quantity: 1.5m,
            ProfitLoss: 1000m,
            Fee: 10m,
            FundingFee: 0m,
            NetPnL: 990m,
            CloseReason: CloseReason.TakeProfit,
            OpenedAt: baseTime.AddMinutes(-30),
            ClosedAt: baseTime
        );

        _queryServiceMock.Setup(x => x.StreamReportTradesAsync(It.IsAny<ReportFilterDto>(), It.IsAny<CancellationToken>()))
            .Returns(ToAsyncEnumerableHelper(new[] { trade }));

        var service = new AnalyticsReportingService(
            _queryServiceMock.Object, _drawdownCalculator, _streakCalculator, _pnlCalculator,
            _scheduleRepositoryMock.Object, _unitOfWorkMock.Object, _options, _loggerMock.Object);

        // Act
        var csv = await service.ExportTradesToCsvAsync(new ReportFilterDto());

        // Assert
        csv.Should().Contain("TradeId,PositionId,Symbol,Side,EntryPrice,ExitPrice,Quantity,GrossPnL,Fee,FundingFee,NetPnL,CloseReason,OpenedAt,ClosedAt");
        csv.Should().Contain("11111111-1111-1111-1111-111111111111,22222222-2222-2222-2222-222222222222,BTCUSDT,Buy,50000.00000000,51000.00000000,1.50000000,1000.00000000,10.00000000,0.00000000,990.00000000,TakeProfit");
    }

    private static async IAsyncEnumerable<T> ToAsyncEnumerableHelper<T>(IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    [Fact]
    public async Task SaveReportScheduleAsync_NewSchedule_ShouldCreateAndSave()
    {
        // Arrange
        var scheduleRepository = new ReportScheduleRepositoryTestImpl(_dbContext);
        var unitOfWork = new Mock<IUnitOfWork>();

        var service = new AnalyticsReportingService(
            _queryServiceMock.Object, _drawdownCalculator, _streakCalculator, _pnlCalculator,
            scheduleRepository, unitOfWork.Object, _options, _loggerMock.Object);

        var dto = new ReportScheduleDto(null, "Daily Report", "0 0 * * *", "Daily", "recipient@test.com", "CSV");

        // Act
        var result = await service.SaveReportScheduleAsync(dto);

        // Assert
        result.Id.Should().NotBeNull();
        result.Id!.Value.Should().NotBe(Guid.Empty);
        result.ScheduleName.Should().Be("Daily Report");

        var entity = await _dbContext.ReportSchedules.FindAsync(result.Id.Value);
        entity.Should().NotBeNull();
        entity!.ScheduleName.Should().Be("Daily Report");
        entity.EmailRecipient.Should().Be("recipient@test.com");
    }

    // Decorator Caching Test
    [Fact]
    public async Task CachedAnalyticsReportingService_ShouldCacheGenerateReport()
    {
        // Arrange
        var coreServiceMock = new Mock<IAnalyticsReportingService>();
        var loggerMock = new Mock<ILogger<TradingBot.Infrastructure.Analytics.Services.CachedAnalyticsReportingService>>();

        var emptyReport1 = new PerformanceReportDto(DateTime.UtcNow, null, null, 10000m, 10000m, null!, null!, null!, null!, null!, null!, null!);
        var emptyReport2 = new PerformanceReportDto(DateTime.UtcNow, null, null, 10000m, 15000m, null!, null!, null!, null!, null!, null!, null!);

        coreServiceMock.SetupSequence(x => x.GenerateReportAsync(It.IsAny<ReportFilterDto>(), It.IsAny<decimal?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyReport1)
            .ReturnsAsync(emptyReport2);

        var cachedService = new TradingBot.Infrastructure.Analytics.Services.CachedAnalyticsReportingService(
            coreServiceMock.Object, _memoryCache, _options, loggerMock.Object);

        var filters = new ReportFilterDto();

        // Act & Assert 1: First call -> should be cache miss and call core service
        var r1 = await cachedService.GenerateReportAsync(filters);
        r1.FinalBalance.Should().Be(10000m);

        // Act & Assert 2: Second call -> cache hit, should return cached report (balance 10000m, not 15000m)
        var r2 = await cachedService.GenerateReportAsync(filters);
        r2.FinalBalance.Should().Be(10000m);

        // Act & Assert 3: Call with bypass cache -> should get fresh report from core service
        var r3 = await cachedService.GenerateReportAsync(filters, bypassCache: true);
        r3.FinalBalance.Should().Be(15000m);
    }

    private class ReportScheduleRepositoryTestImpl : RepositoryBase<ReportSchedule>, IReportScheduleRepository
    {
        public ReportScheduleRepositoryTestImpl(TradingDbContext dbContext) : base(dbContext)
        {
        }
    }
}
