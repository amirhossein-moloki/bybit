using System;

namespace TradingBot.Application.Trading.Execution.Services;

public static class SymbolNormalizer
{
    public static string Normalize(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return string.Empty;
        }

        return symbol
            .Replace("/", "")
            .Replace("-", "")
            .Replace(" ", "")
            .Trim()
            .ToUpperInvariant();
    }
}
