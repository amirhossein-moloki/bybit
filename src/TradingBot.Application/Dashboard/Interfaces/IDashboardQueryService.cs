using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Dashboard.DTOs;

namespace TradingBot.Application.Dashboard.Interfaces;

public interface IDashboardQueryService
{
    Task<DashboardOverviewDto> GetOverviewAsync(CancellationToken cancellationToken = default);
}
