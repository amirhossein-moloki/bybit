using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Repositories;

public interface IOrderRepository : IRepository<Order>
{
    // Existing signatures for backward compatibility
    Task UpdateAsync(Order order, CancellationToken cancellationToken = default);
    Task<IEnumerable<Order>> ListAsync(CancellationToken cancellationToken = default);
    Task<Order?> GetByClientOrderIdAsync(string clientOrderId, CancellationToken cancellationToken = default);

    // New signatures specified in the stage prompt
    Task<Order?> GetByExchangeOrderIdAsync(string exchangeOrderId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Order>> GetByStatusAsync(OrderStatus status, CancellationToken cancellationToken = default);
    Task<IEnumerable<Order>> GetOrdersBySymbolAsync(string symbol, CancellationToken cancellationToken = default);
    Task<IEnumerable<Order>> GetOpenOrdersAsync(CancellationToken cancellationToken = default);

    // Pagination for Order as requested by Stage 03 Section 9
    Task<PagedResult<Order>> GetPagedOrdersAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default);

    // Stage 04 methods
    Task<Order?> GetBySignalIdAsync(Guid signalId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Order>> GetPendingReconciliationOrdersAsync(int batchSize, CancellationToken cancellationToken = default);
}
