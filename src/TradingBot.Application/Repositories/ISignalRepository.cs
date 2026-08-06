using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Repositories;

public interface ISignalRepository : IRepository<Signal>
{
    // Existing signatures for backward compatibility
    Task SaveAsync(Signal signal, CancellationToken cancellationToken = default);

    // New signatures specified in the stage prompt
    Task<IEnumerable<Signal>> GetPendingSignalsAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Signal>> GetBySymbolAsync(string symbol, CancellationToken cancellationToken = default);
    Task UpdateStatusAsync(Guid id, SignalStatus status, CancellationToken cancellationToken = default);

    // Pagination for Signal as requested by Stage 03 Section 9
    Task<PagedResult<Signal>> GetPagedSignalsAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default);

    // Duplicate detection check
    Task<bool> ExistsAsync(long channelId, long messageId, CancellationToken cancellationToken = default);
}
