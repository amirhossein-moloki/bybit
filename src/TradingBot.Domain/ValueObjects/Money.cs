using System;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.ValueObjects;

public record Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    public Money(decimal amount, string currency = "USDT")
    {
        if (amount < 0)
        {
            throw new DomainException("Money amount cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            throw new DomainException("Currency cannot be null or empty.");
        }

        Amount = amount;
        Currency = currency.Trim().ToUpperInvariant();
    }

    public override string ToString() => $"{Amount} {Currency}";
}
