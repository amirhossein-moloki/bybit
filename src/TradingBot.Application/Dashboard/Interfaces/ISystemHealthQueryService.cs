using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Dashboard.DTOs;

namespace TradingBot.Application.Dashboard.Interfaces;

public interface ISystemHealthQueryService
{
    Task<SystemHealthOverviewDto> GetOverviewAsync(
        int recentAlertsLimit = 20,
        int recentEventsLimit = 20,
        int healthHistoryLimit = 20,
        CancellationToken cancellationToken = default);
}
