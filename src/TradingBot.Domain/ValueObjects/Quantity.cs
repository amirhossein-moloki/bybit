using System;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.ValueObjects;

public record Quantity
{
    public decimal Value { get; }
    public string Unit { get; }

    public Quantity(decimal value, string unit = "BTC")
    {
        if (value <= 0)
        {
            throw new DomainException("Quantity must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(unit))
        {
            throw new DomainException("Quantity unit cannot be null or empty.");
        }

        Value = value;
        Unit = unit.Trim().ToUpperInvariant();
    }

    public override string ToString() => $"{Value} {Unit}";
}
