using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Repositories;

public interface ITradeRepository : IRepository<Trade>
{
    // Existing signatures for backward compatibility
    Task SaveAsync(Trade trade, CancellationToken cancellationToken = default);

    // New signatures specified in the stage prompt
    Task<IEnumerable<Trade>> GetTradeHistoryAsync(string symbol, CancellationToken cancellationToken = default);
    Task<ProfitLossReport> GetProfitLossReportAsync(CancellationToken cancellationToken = default);

    // Pagination for Trade as requested by Stage 03 Section 9
    Task<PagedResult<Trade>> GetPagedTradesAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default);
}
