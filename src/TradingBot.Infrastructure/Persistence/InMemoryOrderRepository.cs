using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Interfaces.Persistence;
using TradingBot.Domain.Entities;

namespace TradingBot.Infrastructure.Persistence;

public class InMemoryOrderRepository : IOrderRepository
{
    private static readonly ConcurrentDictionary<Guid, Order> _orders = new();

    public Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        _orders[order.Id] = order;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        _orders[order.Id] = order;
        return Task.CompletedTask;
    }

    public Task<IEnumerable<Order>> ListAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IEnumerable<Order>>(_orders.Values.ToList());
    }

    public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _orders.TryGetValue(id, out var order);
        return Task.FromResult<Order?>(order);
    }

    public Task<Order?> GetByClientOrderIdAsync(string clientOrderId, CancellationToken cancellationToken = default)
    {
        var order = _orders.Values.FirstOrDefault(o => o.ClientOrderId == clientOrderId);
        return Task.FromResult<Order?>(order);
    }
}
