using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Analytics.DTOs;

namespace TradingBot.Application.Analytics.Interfaces;

public interface IPerformanceAnalyticsQueryService
{
    Task<IReadOnlyList<AnalyticsTradeDto>> GetCompletedTradesAsync(
        GetAnalyticsQuery query,
        CancellationToken cancellationToken = default);
}
