using System;
using System.Collections.Generic;

namespace TradingBot.Application.Models;

public class SignalDetectionSettings
{
    public int MinimumScore { get; set; } = 60;
    public List<string> SupportedSymbols { get; set; } = new() { "BTCUSDT", "ETHUSDT", "SOLUSDT", "XRPUSDT", "BNBUSDT" };
    public Dictionary<string, string> SymbolAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        { "BTC", "BTCUSDT" },
        { "ETH", "ETHUSDT" },
        { "SOL", "SOLUSDT" },
        { "XRP", "XRPUSDT" },
        { "BNB", "BNBUSDT" }
    };
    public DetectionRules DetectionRules { get; set; } = new();
}
