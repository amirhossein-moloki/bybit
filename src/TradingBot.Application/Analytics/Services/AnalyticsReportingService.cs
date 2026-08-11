using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBot.Application.Analytics.Configuration;
using TradingBot.Application.Analytics.DTOs;
using TradingBot.Application.Analytics.Interfaces;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Application.Analytics.Services;

public class AnalyticsReportingService : IAnalyticsReportingService
{
    private readonly IAnalyticsReportingQueryService _queryService;
    private readonly DrawdownCalculator _drawdownCalculator;
    private readonly StreakCalculator _streakCalculator;
    private readonly PnLCalculator _pnlCalculator;
    private readonly IReportScheduleRepository _scheduleRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly AnalyticsReportOptions _options;
    private readonly ILogger<AnalyticsReportingService> _logger;

    public AnalyticsReportingService(
        IAnalyticsReportingQueryService queryService,
        DrawdownCalculator drawdownCalculator,
        StreakCalculator streakCalculator,
        PnLCalculator pnlCalculator,
        IReportScheduleRepository scheduleRepository,
        IUnitOfWork unitOfWork,
        IOptions<AnalyticsReportOptions> options,
        ILogger<AnalyticsReportingService> logger)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _drawdownCalculator = drawdownCalculator ?? throw new ArgumentNullException(nameof(drawdownCalculator));
        _streakCalculator = streakCalculator ?? throw new ArgumentNullException(nameof(streakCalculator));
        _pnlCalculator = pnlCalculator ?? throw new ArgumentNullException(nameof(pnlCalculator));
        _scheduleRepository = scheduleRepository ?? throw new ArgumentNullException(nameof(scheduleRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PerformanceReportDto> GenerateReportAsync(
        ReportFilterDto filters,
        decimal? initialBalance = null,
        bool bypassCache = false,
        CancellationToken cancellationToken = default)
    {
        // Under the decorator pattern, this core service doesn't manage caching.
        // It just builds the report. Caching is handled in the decorator.
        var trades = await _queryService.GetReportTradesAsync(filters, cancellationToken);
        decimal balance = initialBalance ?? _options.DefaultInitialBalance;
        return GenerateReportInternal(trades, filters, balance);
    }

    public async Task<IReadOnlyList<EquityPointDto>> GetEquityCurveAsync(
        ReportFilterDto filters,
        decimal? initialBalance = null,
        CancellationToken cancellationToken = default)
    {
        var trades = await _queryService.GetReportTradesAsync(filters, cancellationToken);
        decimal balance = initialBalance ?? _options.DefaultInitialBalance;
        return CalculateEquityCurve(trades, balance);
    }

    public async Task<IReadOnlyList<PeriodAggregationDto>> GetHistoricalAggregationAsync(
        ReportFilterDto filters,
        AggregationPeriod period,
        CancellationToken cancellationToken = default)
    {
        var trades = await _queryService.GetReportTradesAsync(filters, cancellationToken);

        if (trades.Count == 0)
        {
            return Array.Empty<PeriodAggregationDto>();
        }

        IEnumerable<IGrouping<string, ReportTradeDto>> grouped;

        switch (period)
        {
            case AggregationPeriod.Daily:
                grouped = trades.GroupBy(t => t.ClosedAt!.Value.Date.ToString("yyyy-MM-dd"));
                break;
            case AggregationPeriod.Weekly:
                // Group by start-of-week Monday
                grouped = trades.GroupBy(t =>
                {
                    var date = t.ClosedAt!.Value.Date;
                    int diff = (7 + (date.DayOfWeek - DayOfWeek.Monday)) % 7;
                    var monday = date.AddDays(-1 * diff);
                    return monday.ToString("yyyy-MM-dd");
                });
                break;
            case AggregationPeriod.Monthly:
                grouped = trades.GroupBy(t => t.ClosedAt!.Value.ToString("yyyy-MM"));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(period), period, null);
        }

        var aggregations = new List<PeriodAggregationDto>();

        foreach (var group in grouped)
        {
            var periodLabel = group.Key;
            var groupTrades = group.ToList();

            DateTime periodStart;
            DateTime periodEnd;

            if (period == AggregationPeriod.Daily)
            {
                periodStart = DateTime.Parse(periodLabel).ToUniversalTime();
                periodEnd = periodStart.AddDays(1).AddTicks(-1);
            }
            else if (period == AggregationPeriod.Weekly)
            {
                periodStart = DateTime.Parse(periodLabel).ToUniversalTime();
                periodEnd = periodStart.AddDays(7).AddTicks(-1);
            }
            else // Monthly
            {
                var year = int.Parse(periodLabel.Split('-')[0]);
                var month = int.Parse(periodLabel.Split('-')[1]);
                periodStart = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
                periodEnd = periodStart.AddMonths(1).AddTicks(-1);
            }

            int total = groupTrades.Count;
            int wins = groupTrades.Count(t => t.NetPnL > 0);
            int losses = groupTrades.Count(t => t.NetPnL < 0);

            decimal grossProfit = groupTrades.Where(t => t.NetPnL > 0).Sum(t => t.NetPnL);
            decimal grossLoss = groupTrades.Where(t => t.NetPnL < 0).Sum(t => Math.Abs(t.NetPnL));
            decimal netPnL = groupTrades.Sum(t => t.NetPnL);
            decimal totalFees = groupTrades.Sum(t => t.Fee);

            decimal winRate = total > 0 ? (decimal)wins / total * 100m : 0m;

            aggregations.Add(new PeriodAggregationDto(
                PeriodLabel: periodLabel,
                PeriodStart: periodStart,
                PeriodEnd: periodEnd,
                TotalTrades: total,
                WinningTrades: wins,
                LosingTrades: losses,
                WinRate: winRate,
                GrossProfit: grossProfit,
                GrossLoss: grossLoss,
                NetPnL: netPnL,
                TotalFees: totalFees
            ));
        }

        return aggregations.OrderBy(a => a.PeriodStart).ToList();
    }

    public async Task<string> ExportTradesToCsvAsync(
        ReportFilterDto filters,
        CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        sb.AppendLine("TradeId,PositionId,Symbol,Side,EntryPrice,ExitPrice,Quantity,GrossPnL,Fee,FundingFee,NetPnL,CloseReason,OpenedAt,ClosedAt");

        await foreach (var t in _queryService.StreamReportTradesAsync(filters, cancellationToken))
        {
            var tradeId = t.Id.ToString();
            var posId = t.PositionId?.ToString() ?? string.Empty;
            var symbol = t.Symbol;
            var side = t.Side.ToString();
            var entry = t.EntryPrice.ToString("F8");
            var exit = t.ExitPrice?.ToString("F8") ?? string.Empty;
            var qty = t.Quantity.ToString("F8");
            var gross = t.ProfitLoss?.ToString("F8") ?? string.Empty;
            var fee = t.Fee.ToString("F8");
            var funding = t.FundingFee?.ToString("F8") ?? string.Empty;
            var net = t.NetPnL.ToString("F8");
            var reason = t.CloseReason?.ToString() ?? string.Empty;
            var opened = t.OpenedAt?.ToString("o") ?? string.Empty;
            var closed = t.ClosedAt?.ToString("o") ?? string.Empty;

            sb.AppendLine($"{tradeId},{posId},{symbol},{side},{entry},{exit},{qty},{gross},{fee},{funding},{net},{reason},{opened},{closed}");
        }

        return sb.ToString();
    }

    public async Task<ReportScheduleDto> SaveReportScheduleAsync(
        ReportScheduleDto dto,
        CancellationToken cancellationToken = default)
    {
        ReportSchedule? schedule;

        if (dto.Id.HasValue && dto.Id.Value != Guid.Empty)
        {
            schedule = await _scheduleRepository.GetByIdAsync(dto.Id.Value, cancellationToken);
            if (schedule == null)
            {
                throw new DomainException($"Report schedule with ID {dto.Id.Value} not found.");
            }

            schedule.UpdateSchedule(
                dto.ScheduleName,
                dto.CronExpression,
                dto.ReportType,
                dto.EmailRecipient,
                dto.ExportFormat,
                dto.IsActive);

            _scheduleRepository.Update(schedule);
        }
        else
        {
            schedule = new ReportSchedule(
                dto.ScheduleName,
                dto.CronExpression,
                dto.ReportType,
                dto.EmailRecipient,
                dto.ExportFormat,
                dto.IsActive);

            await _scheduleRepository.AddAsync(schedule, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ReportScheduleDto(
            Id: schedule.Id,
            ScheduleName: schedule.ScheduleName,
            CronExpression: schedule.CronExpression,
            ReportType: schedule.ReportType,
            EmailRecipient: schedule.EmailRecipient,
            ExportFormat: schedule.ExportFormat,
            IsActive: schedule.IsActive
        );
    }

    private PerformanceReportDto GenerateReportInternal(
        IReadOnlyList<ReportTradeDto> trades,
        ReportFilterDto filters,
        decimal initialBalance)
    {
        int totalTrades = trades.Count;

        if (totalTrades == 0)
        {
            var emptyMetrics = new PerformanceMetricsDto(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
            var emptyDrawdown = new DrawdownMetricsDto(initialBalance, initialBalance, 0, 0, 0);
            var emptyStreaks = new StreakMetricsDto(0, 0, 0, 0);
            var emptyDuration = new DurationMetricsDto(null, null, null, null, null);
            var emptyLongShort = new LongShortPerformanceDto(
                new SidePerformanceDto(0, 0, 0, 0, 0, 0),
                new SidePerformanceDto(0, 0, 0, 0, 0, 0)
            );

            return new PerformanceReportDto(
                GeneratedAt: DateTime.UtcNow,
                StartDate: filters.StartDate,
                EndDate: filters.EndDate,
                InitialBalance: initialBalance,
                FinalBalance: initialBalance,
                Metrics: emptyMetrics,
                Drawdown: emptyDrawdown,
                Streaks: emptyStreaks,
                Durations: emptyDuration,
                LongShort: emptyLongShort,
                EquityCurve: Array.Empty<EquityPointDto>(),
                DetailedTrades: Array.Empty<ReportTradeDto>()
            );
        }

        var netPnLs = trades.Select(t => t.NetPnL).ToList();

        // 1. Performance Metrics
        int winningTrades = 0;
        int losingTrades = 0;
        int breakevenTrades = 0;

        decimal grossProfit = 0m;
        decimal grossLoss = 0m;
        decimal netPnLSum = 0m;

        decimal largestWin = 0m;
        decimal largestLoss = 0m;

        foreach (var pnl in netPnLs)
        {
            netPnLSum += pnl;

            if (pnl > 0)
            {
                winningTrades++;
                grossProfit += pnl;
                if (pnl > largestWin)
                {
                    largestWin = pnl;
                }
            }
            else if (pnl < 0)
            {
                losingTrades++;
                decimal absLoss = Math.Abs(pnl);
                grossLoss += absLoss;
                if (absLoss > largestLoss)
                {
                    largestLoss = absLoss;
                }
            }
            else
            {
                breakevenTrades++;
            }
        }

        decimal winRate = (decimal)winningTrades / totalTrades * 100m;
        decimal lossRate = (decimal)losingTrades / totalTrades * 100m;

        decimal averagePnL = netPnLSum / totalTrades;
        decimal averageWin = winningTrades > 0 ? grossProfit / winningTrades : 0m;
        decimal averageLoss = losingTrades > 0 ? grossLoss / losingTrades : 0m;

        decimal profitFactor = _pnlCalculator.CalculateProfitFactor(grossProfit, grossLoss);

        var metrics = new PerformanceMetricsDto(
            TotalTrades: totalTrades,
            WinningTrades: winningTrades,
            LosingTrades: losingTrades,
            BreakevenTrades: breakevenTrades,
            WinRate: winRate,
            LossRate: lossRate,
            AverageWin: averageWin,
            AverageLoss: averageLoss,
            LargestWin: largestWin,
            LargestLoss: largestLoss,
            AverageTradePnL: averagePnL,
            ProfitFactor: profitFactor,
            GrossProfit: grossProfit,
            GrossLoss: grossLoss,
            NetPnL: netPnLSum
        );

        // 2. Drawdown Metrics
        var drawdown = _drawdownCalculator.Calculate(netPnLs, initialBalance);

        // 3. Streaks
        var streaks = _streakCalculator.Calculate(netPnLs);

        // 4. Durations
        var validDurations = new List<TimeSpan>();
        var winningDurations = new List<TimeSpan>();
        var losingDurations = new List<TimeSpan>();

        foreach (var trade in trades)
        {
            if (trade.OpenedAt.HasValue && trade.ClosedAt.HasValue)
            {
                var duration = trade.ClosedAt.Value - trade.OpenedAt.Value;
                if (duration >= TimeSpan.Zero)
                {
                    validDurations.Add(duration);
                    if (trade.NetPnL > 0)
                    {
                        winningDurations.Add(duration);
                    }
                    else if (trade.NetPnL < 0)
                    {
                        losingDurations.Add(duration);
                    }
                }
            }
        }

        TimeSpan? averageDuration = validDurations.Count > 0
            ? TimeSpan.FromTicks((long)validDurations.Average(d => d.Ticks))
            : null;

        TimeSpan? shortestDuration = validDurations.Count > 0
            ? validDurations.Min()
            : null;

        TimeSpan? longestDuration = validDurations.Count > 0
            ? validDurations.Max()
            : null;

        TimeSpan? averageWinningDuration = winningDurations.Count > 0
            ? TimeSpan.FromTicks((long)winningDurations.Average(d => d.Ticks))
            : null;

        TimeSpan? averageLosingDuration = losingDurations.Count > 0
            ? TimeSpan.FromTicks((long)losingDurations.Average(d => d.Ticks))
            : null;

        var durationsDto = new DurationMetricsDto(
            AverageDuration: averageDuration,
            ShortestDuration: shortestDuration,
            LongestDuration: longestDuration,
            AverageWinningDuration: averageWinningDuration,
            AverageLosingDuration: averageLosingDuration
        );

        // 5. LongShort
        var longTrades = trades.Where(t => t.Side == OrderSide.Buy).ToList();
        var shortTrades = trades.Where(t => t.Side == OrderSide.Sell).ToList();

        var longDto = CalculateSidePerformance(longTrades);
        var shortDto = CalculateSidePerformance(shortTrades);

        var longShort = new LongShortPerformanceDto(Long: longDto, Short: shortDto);

        // 6. Equity Curve
        var equityCurve = CalculateEquityCurve(trades, initialBalance);

        return new PerformanceReportDto(
            GeneratedAt: DateTime.UtcNow,
            StartDate: filters.StartDate,
            EndDate: filters.EndDate,
            InitialBalance: initialBalance,
            FinalBalance: initialBalance + netPnLSum,
            Metrics: metrics,
            Drawdown: drawdown,
            Streaks: streaks,
            Durations: durationsDto,
            LongShort: longShort,
            EquityCurve: equityCurve,
            DetailedTrades: trades
        );
    }

    private SidePerformanceDto CalculateSidePerformance(List<ReportTradeDto> trades)
    {
        int total = trades.Count;
        if (total == 0)
        {
            return new SidePerformanceDto(0, 0, 0, 0, 0, 0);
        }

        int wins = 0;
        int losses = 0;
        decimal totalPnL = 0m;

        foreach (var t in trades)
        {
            totalPnL += t.NetPnL;

            if (t.NetPnL > 0)
            {
                wins++;
            }
            else if (t.NetPnL < 0)
            {
                losses++;
            }
        }

        decimal winRate = (decimal)wins / total * 100m;
        decimal averagePnL = totalPnL / total;

        return new SidePerformanceDto(
            Trades: total,
            Wins: wins,
            Losses: losses,
            WinRate: winRate,
            TotalPnL: totalPnL,
            AveragePnL: averagePnL
        );
    }

    private IReadOnlyList<EquityPointDto> CalculateEquityCurve(
        IReadOnlyList<ReportTradeDto> trades,
        decimal initialBalance)
    {
        var points = new List<EquityPointDto>();

        decimal currentEquity = initialBalance;
        decimal peakEquity = initialBalance;
        decimal cumulativePnL = 0m;

        for (int i = 0; i < trades.Count; i++)
        {
            var t = trades[i];
            decimal pnl = t.NetPnL;

            currentEquity += pnl;
            cumulativePnL += pnl;

            if (currentEquity > peakEquity)
            {
                peakEquity = currentEquity;
            }

            decimal drawdown = peakEquity - currentEquity;
            decimal drawdownPercent = peakEquity > 0 ? (drawdown / peakEquity) * 100m : 0m;

            points.Add(new EquityPointDto(
                TradeIndex: i + 1,
                TradeId: t.Id,
                ClosedAt: t.ClosedAt ?? DateTime.UtcNow,
                NetPnL: pnl,
                CumulativePnL: cumulativePnL,
                Equity: currentEquity,
                Drawdown: drawdown,
                DrawdownPercentage: drawdownPercent,
                PeakEquity: peakEquity
            ));
        }

        return points;
    }
}
