using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Persistence.Context;

namespace TradingBot.Persistence.Repositories;

public class ProcessedEventRepository : RepositoryBase<ProcessedEvent>, IProcessedEventRepository
{
    public ProcessedEventRepository(TradingDbContext dbContext) : base(dbContext)
    {
    }

    public async Task<bool> ExistsAsync(string eventId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return false;
        return await DbContext.ProcessedEvents.AnyAsync(x => x.EventId == eventId, cancellationToken);
    }
}
