using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Persistence.Context;

namespace TradingBot.Persistence.Repositories;

public class SignalRepository : RepositoryBase<Signal>, ISignalRepository
{
    public SignalRepository(TradingDbContext dbContext) : base(dbContext)
    {
    }

    // Backward compatibility save method
    public async Task SaveAsync(Signal signal, CancellationToken cancellationToken = default)
    {
        var existing = await DbContext.Signals.FindAsync(new object?[] { signal.Id }, cancellationToken);
        if (existing == null)
        {
            await AddAsync(signal, cancellationToken);
        }
        else
        {
            // Detach existing if tracking is different to avoid conflict
            DbContext.Entry(existing).State = EntityState.Detached;
            Update(signal);
        }
    }

    // New methods from ISignalRepository
    public async Task<IEnumerable<Signal>> GetPendingSignalsAsync(CancellationToken cancellationToken = default)
    {
        var pendingStatuses = new[] { SignalStatus.Received, SignalStatus.Parsed, SignalStatus.Validated };
        return await DbContext.Signals
            .AsNoTracking()
            .Where(s => pendingStatuses.Contains(s.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Signal>> GetBySymbolAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        return await DbContext.Signals
            .AsNoTracking()
            .Where(s => s.Symbol == normalizedSymbol)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateStatusAsync(Guid id, SignalStatus status, CancellationToken cancellationToken = default)
    {
        var signal = await GetByIdAsync(id, cancellationToken);
        if (signal != null)
        {
            switch (status)
            {
                case SignalStatus.Parsed:
                    signal.MarkParsed();
                    break;
                case SignalStatus.Validated:
                    signal.MarkValidated();
                    break;
                case SignalStatus.Rejected:
                    signal.MarkRejected();
                    break;
                case SignalStatus.Executed:
                    signal.MarkExecuted();
                    break;
                case SignalStatus.Received:
                    // Received is initial, no action needed or direct assign if already received
                    break;
            }
            Update(signal);
        }
    }

    public async Task<PagedResult<Signal>> GetPagedSignalsAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        return await GetPagedAsync(pageNumber, pageSize, cancellationToken);
    }

    // Duplicate detection check
    public async Task<bool> ExistsAsync(long channelId, long messageId, CancellationToken cancellationToken = default)
    {
        return await DbContext.Signals
            .AnyAsync(s => s.TelegramChannelId == channelId && s.TelegramMessageId == messageId, cancellationToken);
    }
}
