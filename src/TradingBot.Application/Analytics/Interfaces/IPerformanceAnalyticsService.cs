using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Analytics.DTOs;

namespace TradingBot.Application.Analytics.Interfaces;

public interface IPerformanceAnalyticsService
{
    Task<PerformanceMetricsDto> GetPerformanceMetricsAsync(GetAnalyticsQuery query, CancellationToken cancellationToken = default);
    Task<DrawdownMetricsDto> GetDrawdownMetricsAsync(GetAnalyticsQuery query, CancellationToken cancellationToken = default);
    Task<StreakMetricsDto> GetStreakMetricsAsync(GetAnalyticsQuery query, CancellationToken cancellationToken = default);
    Task<DurationMetricsDto> GetDurationMetricsAsync(GetAnalyticsQuery query, CancellationToken cancellationToken = default);
    Task<LongShortPerformanceDto> GetLongShortPerformanceAsync(GetAnalyticsQuery query, CancellationToken cancellationToken = default);
}
