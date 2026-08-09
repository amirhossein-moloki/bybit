using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Dashboard.DTOs;

namespace TradingBot.Application.Dashboard.Interfaces;

public interface ITradingDashboardQueryService
{
    Task<TradingDashboardOverviewDto> GetOverviewAsync(
        TradingDashboardQuery query,
        CancellationToken cancellationToken = default);
}
