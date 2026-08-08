using System;
using System.Collections.Generic;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Models;

namespace TradingBot.Application.Trading.Execution.Services;

public class TestExchangeInstrumentRules : IExchangeInstrumentRules
{
    private readonly Dictionary<string, InstrumentRules> _rules = new(StringComparer.OrdinalIgnoreCase);

    public TestExchangeInstrumentRules()
    {
        // Register some default rules for common symbols
        _rules["BTCUSDT"] = new InstrumentRules
        {
            Symbol = "BTCUSDT",
            TickSize = 0.10m,
            QuantityStep = 0.001m,
            MinQuantity = 0.001m,
            MaxQuantity = 100m,
            MinNotional = 5.0m,
            PricePrecision = 1,
            QuantityPrecision = 3
        };

        _rules["ETHUSDT"] = new InstrumentRules
        {
            Symbol = "ETHUSDT",
            TickSize = 0.01m,
            QuantityStep = 0.01m,
            MinQuantity = 0.01m,
            MaxQuantity = 1000m,
            MinNotional = 5.0m,
            PricePrecision = 2,
            QuantityPrecision = 2
        };

        _rules["SOLUSDT"] = new InstrumentRules
        {
            Symbol = "SOLUSDT",
            TickSize = 0.01m,
            QuantityStep = 0.1m,
            MinQuantity = 0.1m,
            MaxQuantity = 5000m,
            MinNotional = 5.0m,
            PricePrecision = 2,
            QuantityPrecision = 1
        };
    }

    public InstrumentRules? GetInstrumentRules(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return null;
        }

        var normalized = SymbolNormalizer.Normalize(symbol);
        if (_rules.TryGetValue(normalized, out var rule))
        {
            return rule;
        }

        // Return a standard reasonable default for other symbols to allow execution testing,
        // unless it's explicitly simulated as missing (e.g., symbol containing "MISSING" or similar)
        if (normalized.Contains("MISSING") || normalized.Contains("UNKNOWN"))
        {
            return null;
        }

        return new InstrumentRules
        {
            Symbol = normalized,
            TickSize = 0.01m,
            QuantityStep = 0.001m,
            MinQuantity = 0.001m,
            MaxQuantity = 10000m,
            MinNotional = 1.0m,
            PricePrecision = 2,
            QuantityPrecision = 3
        };
    }

    public void AddOrUpdateRule(InstrumentRules rules)
    {
        if (rules == null) throw new ArgumentNullException(nameof(rules));
        var normalized = SymbolNormalizer.Normalize(rules.Symbol);
        _rules[normalized] = rules;
    }
}
