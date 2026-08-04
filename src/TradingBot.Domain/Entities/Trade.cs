using System;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.Entities;

public class Trade
{
    public Guid Id { get; private set; }
    public string TradeId { get; private set; }
    public string OrderId { get; private set; }
    public string Symbol { get; private set; }
    public SignalType Side { get; private set; }
    public decimal Price { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal Fee { get; private set; }
    public string FeeAsset { get; private set; }
    public DateTime ExecutedAt { get; private set; }

    public Trade(string tradeId, string orderId, string symbol, SignalType side, decimal price, decimal quantity, decimal fee, string feeAsset)
    {
        if (string.IsNullOrWhiteSpace(tradeId))
        {
            throw new DomainException("TradeId cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(orderId))
        {
            throw new DomainException("OrderId cannot be empty.");
        }

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
        TradeId = tradeId;
        OrderId = orderId;
        Symbol = symbol.ToUpperInvariant();
        Side = side;
        Price = price;
        Quantity = quantity;
        Fee = fee;
        FeeAsset = feeAsset;
        ExecutedAt = DateTime.UtcNow;
    }
}
