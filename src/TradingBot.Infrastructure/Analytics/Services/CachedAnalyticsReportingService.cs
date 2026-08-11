using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBot.Application.Analytics.Configuration;
using TradingBot.Application.Analytics.DTOs;
using TradingBot.Application.Analytics.Interfaces;

namespace TradingBot.Infrastructure.Analytics.Services;

public class CachedAnalyticsReportingService : IAnalyticsReportingService
{
    private readonly IAnalyticsReportingService _innerService;
    private readonly IMemoryCache _memoryCache;
    private readonly AnalyticsReportOptions _options;
    private readonly ILogger<CachedAnalyticsReportingService> _logger;

    public CachedAnalyticsReportingService(
        IAnalyticsReportingService innerService,
        IMemoryCache memoryCache,
        IOptions<AnalyticsReportOptions> options,
        ILogger<CachedAnalyticsReportingService> logger)
    {
        _innerService = innerService ?? throw new ArgumentNullException(nameof(innerService));
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PerformanceReportDto> GenerateReportAsync(
        ReportFilterDto filters,
        decimal? initialBalance = null,
        bool bypassCache = false,
        CancellationToken cancellationToken = default)
    {
        decimal balance = initialBalance ?? _options.DefaultInitialBalance;

        if (!_options.EnableCaching || bypassCache)
        {
            _logger.LogInformation("Caching bypassed or disabled. Delegating directly to inner reporting service.");
            return await _innerService.GenerateReportAsync(filters, balance, bypassCache, cancellationToken);
        }

        var cacheKey = GenerateCacheKey(filters, balance);

        if (_memoryCache.TryGetValue<PerformanceReportDto>(cacheKey, out var cachedReport))
        {
            _logger.LogInformation("Performance report cache hit.");
            return cachedReport!;
        }

        _logger.LogInformation("Performance report cache miss. Fetching from inner reporting service.");
        var report = await _innerService.GenerateReportAsync(filters, balance, bypassCache, cancellationToken);

        _memoryCache.Set(cacheKey, report, TimeSpan.FromMinutes(_options.CacheTtlMinutes));

        return report;
    }

    public Task<IReadOnlyList<EquityPointDto>> GetEquityCurveAsync(
        ReportFilterDto filters,
        decimal? initialBalance = null,
        CancellationToken cancellationToken = default)
    {
        return _innerService.GetEquityCurveAsync(filters, initialBalance, cancellationToken);
    }

    public Task<IReadOnlyList<PeriodAggregationDto>> GetHistoricalAggregationAsync(
        ReportFilterDto filters,
        AggregationPeriod period,
        CancellationToken cancellationToken = default)
    {
        return _innerService.GetHistoricalAggregationAsync(filters, period, cancellationToken);
    }

    public Task<string> ExportTradesToCsvAsync(
        ReportFilterDto filters,
        CancellationToken cancellationToken = default)
    {
        return _innerService.ExportTradesToCsvAsync(filters, cancellationToken);
    }

    public Task<ReportScheduleDto> SaveReportScheduleAsync(
        ReportScheduleDto scheduleDto,
        CancellationToken cancellationToken = default)
    {
        return _innerService.SaveReportScheduleAsync(scheduleDto, cancellationToken);
    }

    private string GenerateCacheKey(ReportFilterDto filters, decimal initialBalance)
    {
        return $"report:{filters.StartDate?.Ticks}:{filters.EndDate?.Ticks}:{filters.Symbol}:{filters.Side}:{filters.MinPnL}:{filters.MaxPnL}:{filters.CloseReason}:{initialBalance}";
    }
}
