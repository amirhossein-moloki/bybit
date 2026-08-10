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

    public async Task<bool> TryRegisterEventAsync(string eventId, string eventType, Guid? positionId = null, string? exchangeOrderId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventId)) return false;

        if (await ExistsAsync(eventId, cancellationToken))
        {
            return false;
        }

        try
        {
            var processedEvent = new ProcessedEvent(eventId, eventType, positionId, exchangeOrderId);
            await AddAsync(processedEvent, cancellationToken);
            await DbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is DbUpdateException || ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true || ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Concurrent insert caught by DB unique constraint
            return false;
        }
    }
}
