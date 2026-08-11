using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Analytics.DTOs;

namespace TradingBot.Application.Analytics.Interfaces;

public interface IAnalyticsReportingQueryService
{
    Task<IReadOnlyList<ReportTradeDto>> GetReportTradesAsync(
        ReportFilterDto filters,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<ReportTradeDto> StreamReportTradesAsync(
        ReportFilterDto filters,
        CancellationToken cancellationToken = default);
}
