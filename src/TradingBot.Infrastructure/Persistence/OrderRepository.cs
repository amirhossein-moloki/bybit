using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Interfaces.Persistence;
using TradingBot.Domain.Entities;
using TradingBot.Persistence.Context;

namespace TradingBot.Infrastructure.Persistence;

public class OrderRepository : IOrderRepository
{
    private readonly TradingDbContext _dbContext;

    public OrderRepository(TradingDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        await _dbContext.Orders.AddAsync(order, cancellationToken);
    }

    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Orders.FindAsync(new object?[] { id }, cancellationToken);
    }

    public async Task UpdateAsync(Order order, CancellationToken cancellationToken = default)
    {
        _dbContext.Orders.Update(order);
        await Task.CompletedTask;
    }

    public async Task<IEnumerable<Order>> ListAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Orders.ToListAsync(cancellationToken);
    }

    public async Task<Order?> GetByClientOrderIdAsync(string clientOrderId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Orders.FirstOrDefaultAsync(o => o.ClientOrderId == clientOrderId, cancellationToken);
    }
}
