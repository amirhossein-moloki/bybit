using System;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.Entities;

public class Order
{
    public Guid Id { get; private set; }
    public string ClientOrderId { get; private set; }
    public string Symbol { get; private set; }
    public OrderType Type { get; private set; }
    public SignalType Side { get; private set; }
    public decimal Price { get; private set; }
    public decimal Quantity { get; private set; }
    public OrderStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public Order(string clientOrderId, string symbol, OrderType type, SignalType side, decimal price, decimal quantity)
    {
        if (string.IsNullOrWhiteSpace(clientOrderId))
        {
            throw new DomainException("ClientOrderId cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new DomainException("Symbol cannot be empty.");
        }

        if (price <= 0 && type == OrderType.Limit)
        {
            throw new DomainException("Price must be greater than zero for Limit orders.");
        }

        if (quantity <= 0)
        {
            throw new DomainException("Quantity must be greater than zero.");
        }

        Id = Guid.NewGuid();
        ClientOrderId = clientOrderId;
        Symbol = symbol.ToUpperInvariant();
        Type = type;
        Side = side;
        Price = price;
        Quantity = quantity;
        Status = OrderStatus.New;
        CreatedAt = DateTime.UtcNow;
    }

    public void UpdateStatus(OrderStatus newStatus)
    {
        if (Status == OrderStatus.Filled || Status == OrderStatus.Cancelled || Status == OrderStatus.Rejected)
        {
            throw new DomainException($"Cannot change state of order from {Status} to {newStatus}.");
        }

        Status = newStatus;
        UpdatedAt = DateTime.UtcNow;
    }
}
