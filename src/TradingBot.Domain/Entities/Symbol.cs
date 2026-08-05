using System;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.Entities;

public class Symbol
{
    public Guid Id { get; private set; }
    public string Exchange { get; private set; }
    public string SymbolCode { get; private set; }
    public string BaseAsset { get; private set; }
    public string QuoteAsset { get; private set; }
    public decimal TickSize { get; private set; }
    public decimal QuantityStep { get; private set; }
    public decimal MinQuantity { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Required for EF Core
    private Symbol()
    {
        Id = Guid.Empty;
        Exchange = string.Empty;
        SymbolCode = string.Empty;
        BaseAsset = string.Empty;
        QuoteAsset = string.Empty;
        CreatedAt = DateTime.UtcNow;
    }

    public Symbol(string exchange, string symbolCode, string baseAsset, string quoteAsset, decimal tickSize, decimal quantityStep, decimal minQuantity)
    {
        if (string.IsNullOrWhiteSpace(exchange))
        {
            throw new DomainException("Exchange cannot be null or empty.");
        }

        if (string.IsNullOrWhiteSpace(symbolCode))
        {
            throw new DomainException("SymbolCode cannot be null or empty.");
        }

        if (string.IsNullOrWhiteSpace(baseAsset))
        {
            throw new DomainException("BaseAsset cannot be null or empty.");
        }

        if (string.IsNullOrWhiteSpace(quoteAsset))
        {
            throw new DomainException("QuoteAsset cannot be null or empty.");
        }

        if (tickSize <= 0)
        {
            throw new DomainException("TickSize must be greater than zero.");
        }

        if (quantityStep <= 0)
        {
            throw new DomainException("QuantityStep must be greater than zero.");
        }

        if (minQuantity <= 0)
        {
            throw new DomainException("MinQuantity must be greater than zero.");
        }

        Id = Guid.NewGuid();
        Exchange = exchange.Trim().ToUpperInvariant();
        SymbolCode = symbolCode.Trim().ToUpperInvariant();
        BaseAsset = baseAsset.Trim().ToUpperInvariant();
        QuoteAsset = quoteAsset.Trim().ToUpperInvariant();
        TickSize = tickSize;
        QuantityStep = quantityStep;
        MinQuantity = minQuantity;
        CreatedAt = DateTime.UtcNow;
    }
}
