using System;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.ValueObjects;

public record Symbol
{
    public string Value { get; }

    public Symbol(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainException("Symbol cannot be null or empty.");
        }

        Value = value.Trim().ToUpperInvariant();

        if (Value.Length < 3)
        {
            throw new DomainException("Symbol must be at least 3 characters long.");
        }
    }

    public override string ToString() => Value;
}
