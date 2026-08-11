using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Analytics.DTOs;

namespace TradingBot.Application.Analytics.Interfaces;

public interface IAnalyticsReportingService
{
    Task<PerformanceReportDto> GenerateReportAsync(
        ReportFilterDto filters,
        decimal? initialBalance = null,
        bool bypassCache = false,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EquityPointDto>> GetEquityCurveAsync(
        ReportFilterDto filters,
        decimal? initialBalance = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PeriodAggregationDto>> GetHistoricalAggregationAsync(
        ReportFilterDto filters,
        AggregationPeriod period,
        CancellationToken cancellationToken = default);

    Task<string> ExportTradesToCsvAsync(
        ReportFilterDto filters,
        CancellationToken cancellationToken = default);

    Task<ReportScheduleDto> SaveReportScheduleAsync(
        ReportScheduleDto scheduleDto,
        CancellationToken cancellationToken = default);
}
