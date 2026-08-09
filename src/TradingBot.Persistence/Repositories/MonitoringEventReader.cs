using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Monitoring;
using TradingBot.Application.Monitoring.Configuration;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Persistence.Context;

namespace TradingBot.Persistence.Repositories;

public class MonitoringEventReader : IMonitoringEventReader
{
    private readonly TradingDbContext _dbContext;
    private readonly MonitoringOptions _options;

    public MonitoringEventReader(TradingDbContext dbContext, MonitoringOptions options)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<PagedResult<MonitoringEvent>> GetEventsAsync(
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
        CancellationToken cancellationToken = default)
    {
        // Enforce pagination bounds
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 10;

        // Use a configurable or safe max page size limit (Section 51)
        int maxPageSize = 100;
        if (pageSize > maxPageSize)
        {
            pageSize = maxPageSize;
        }

        var query = _dbContext.MonitoringEvents.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            query = query.Where(x => x.EventType.ToLower() == eventType.Trim().ToLower());
        }

        if (!string.IsNullOrWhiteSpace(severity))
        {
            query = query.Where(x => x.Severity.ToLower() == severity.Trim().ToLower());
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            query = query.Where(x => x.Source.ToLower() == source.Trim().ToLower());
        }

        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            query = query.Where(x => x.CorrelationId == correlationId.Trim());
        }

        if (orderId.HasValue && orderId.Value != Guid.Empty)
        {
            query = query.Where(x => x.OrderId == orderId.Value);
        }

        if (positionId.HasValue && positionId.Value != Guid.Empty)
        {
            query = query.Where(x => x.PositionId == positionId.Value);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.Status.ToLower() == status.Trim().ToLower());
        }

        if (fromUtc.HasValue)
        {
            query = query.Where(x => x.Timestamp >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(x => x.Timestamp <= toUtc.Value);
        }

        // Newest First Ordering (Section 29)
        query = query.OrderByDescending(x => x.Timestamp);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<MonitoringEvent>(items, totalCount, pageNumber, pageSize);
    }
}
