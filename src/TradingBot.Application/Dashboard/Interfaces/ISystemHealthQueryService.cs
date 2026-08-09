using System;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Dashboard.DTOs;
using TradingBot.Application.Repositories;

namespace TradingBot.Application.Dashboard.Interfaces;

public interface ISystemHealthQueryService
{
    Task<SystemHealthOverviewDto> GetOverviewAsync(
        int recentAlertsLimit = 20,
        int recentEventsLimit = 20,
        int healthHistoryLimit = 20,
        CancellationToken cancellationToken = default);

    Task<PagedResult<AlertDto>> GetAlertsAsync(
        string? severity = null,
        string? source = null,
        string? type = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    Task<PagedResult<RecentEventDto>> GetEventsAsync(
        string? type = null,
        string? severity = null,
        string? source = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    Task<PagedResult<HealthHistoryRecordDto>> GetHealthHistoryAsync(
        string? service = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);
}
