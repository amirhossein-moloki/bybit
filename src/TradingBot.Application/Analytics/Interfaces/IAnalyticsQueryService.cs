using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Analytics.DTOs;

namespace TradingBot.Application.Analytics.Interfaces;

public interface IAnalyticsQueryService
{
    Task<TradeStatisticsDto> GetTradeStatisticsAsync(
        GetTradeStatisticsQuery query,
        CancellationToken cancellationToken = default);
}
