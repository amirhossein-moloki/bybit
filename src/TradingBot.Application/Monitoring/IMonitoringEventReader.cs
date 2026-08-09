using System;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Monitoring;

public interface IMonitoringEventReader
{
    Task<PagedResult<MonitoringEvent>> GetEventsAsync(
        string? eventType = null,
        string? severity = null,
        string? source = null,
        string? correlationId = null,
        Guid? orderId = null,
        Guid? positionId = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        string? status = null,
        int pageNumber = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);
}
