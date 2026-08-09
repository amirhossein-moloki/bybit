using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Persistence.Context;

namespace TradingBot.Persistence.Repositories;

public class AlertRepository : RepositoryBase<Alert>, IAlertRepository
{
    public AlertRepository(TradingDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<Alert?> GetActiveByDeduplicationKeyAsync(string deduplicationKey, CancellationToken cancellationToken = default)
    {
        return await DbContext.Alerts
            .FirstOrDefaultAsync(x => x.DeduplicationKey == deduplicationKey && x.Status != "Resolved", cancellationToken);
    }

    public async Task<IEnumerable<Alert>> GetActiveAlertsAsync(CancellationToken cancellationToken = default)
    {
        return await DbContext.Alerts
            .Where(x => x.Status != "Resolved")
            .ToListAsync(cancellationToken);
    }
}
