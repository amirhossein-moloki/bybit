using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Interfaces;

public interface IOrderService
{
    Task<Order> CreateOrderAsync(string symbol, OrderSide side, OrderType type, decimal quantity, decimal price, CancellationToken cancellationToken = default);
    Task<Order> CancelOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<Order?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Order>> GetOrdersAsync(CancellationToken cancellationToken = default);
}
