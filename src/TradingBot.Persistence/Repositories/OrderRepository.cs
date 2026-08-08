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

public class OrderRepository : RepositoryBase<Order>, IOrderRepository
{
    public OrderRepository(TradingDbContext dbContext) : base(dbContext)
    {
    }

    // Backward compatibility methods
    public async Task UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        Update(order);
        await Task.CompletedTask;
    }

    public async Task<IEnumerable<Order>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await GetAllAsync(cancellationToken);
    }

    public async Task<Order?> GetByClientOrderIdAsync(string clientOrderId, CancellationToken cancellationToken = default)
    {
        return await DbContext.Orders
            .FirstOrDefaultAsync(o => o.ClientOrderId == clientOrderId, cancellationToken);
    }

    // New methods from IOrderRepository
    public async Task<Order?> GetByExchangeOrderIdAsync(string exchangeOrderId, CancellationToken cancellationToken = default)
    {
        return await DbContext.Orders
            .FirstOrDefaultAsync(o => o.ExchangeOrderId == exchangeOrderId, cancellationToken);
    }

    public async Task<IEnumerable<Order>> GetByStatusAsync(OrderStatus status, CancellationToken cancellationToken = default)
    {
        return await DbContext.Orders
            .AsNoTracking()
            .Where(o => o.Status == status)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Order>> GetOrdersBySymbolAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var normalizedSymbol = symbol.Trim().ToUpperInvariant();
        return await DbContext.Orders
            .AsNoTracking()
            .Where(o => o.Symbol.Value == normalizedSymbol)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Order>> GetOpenOrdersAsync(CancellationToken cancellationToken = default)
    {
        var openStatuses = new[] { OrderStatus.Created, OrderStatus.Submitted, OrderStatus.Accepted, OrderStatus.PartiallyFilled };
        return await DbContext.Orders
            .AsNoTracking()
            .Where(o => openStatuses.Contains(o.Status))
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<Order>> GetPagedOrdersAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        return await GetPagedAsync(pageNumber, pageSize, cancellationToken);
    }

    // Stage 04 methods
    public async Task<Order?> GetBySignalIdAsync(Guid signalId, CancellationToken cancellationToken = default)
    {
        return await DbContext.Orders
            .FirstOrDefaultAsync(o => o.SignalId == signalId, cancellationToken);
    }

    public async Task<IEnumerable<Order>> GetPendingReconciliationOrdersAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var openStatuses = new[] {
            OrderStatus.Pending,
            OrderStatus.Submitting,
            OrderStatus.Submitted,
            OrderStatus.Accepted,
            OrderStatus.New,
            OrderStatus.PartiallyFilled,
            OrderStatus.Unknown
        };

        return await DbContext.Orders
            .Where(o => openStatuses.Contains(o.Status))
            .OrderBy(o => o.CreatedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);
    }
}
