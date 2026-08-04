using System;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.Entities;

public class Signal
{
    public Guid Id { get; private set; }
    public string Symbol { get; private set; }
    public SignalType Type { get; private set; }
    public decimal Price { get; private set; }
    public decimal Quantity { get; private set; }
    public DateTime CreatedAt { get; private set; }

    public Signal(string symbol, SignalType type, decimal price, decimal quantity)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new DomainException("Symbol cannot be empty.");
        }

        if (price <= 0)
        {
            throw new DomainException("Price must be greater than zero.");
        }

        if (quantity <= 0)
        {
            throw new DomainException("Quantity must be greater than zero.");
        }

        Id = Guid.NewGuid();
        Symbol = symbol.ToUpperInvariant();
        Type = type;
        Price = price;
        Quantity = quantity;
        CreatedAt = DateTime.UtcNow;
    }
}
