using System;
using System.Collections.Generic;

namespace TradingBot.Application.Models;

public class SignalDetectionSettings
{
    public int MinimumScore { get; set; } = 60;
    public List<string> SupportedSymbols { get; set; } = new()
    {
        "BTCUSDT", "ETHUSDT", "SOLUSDT", "XRPUSDT", "BNBUSDT",
        "GBPUSDT", "EURUSDT", "NZDUSDT", "AUDUSDT", "USDCAD", "USDJPY", "XAUUSDT",
        "GBPUSD", "EURUSD", "NZDUSD", "AUDUSD", "XAUUSD"
    };
    public Dictionary<string, string> SymbolAliases { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        { "BTC", "BTCUSDT" },
        { "ETH", "ETHUSDT" },
        { "SOL", "SOLUSDT" },
        { "XRP", "XRPUSDT" },
        { "BNB", "BNBUSDT" },
        { "GBP/USD", "GBPUSDT" },
        { "EUR/USD", "EURUSDT" },
        { "NZD/USD", "NZDUSDT" },
        { "AUD/USD", "AUDUSDT" },
        { "USD/CAD", "USDCAD" },
        { "USD/JPY", "USDJPY" },
        { "XAU/USD", "XAUUSDT" },
        { "GOLD", "XAUUSDT" },
        { "GBPUSD", "GBPUSDT" },
        { "EURUSD", "EURUSDT" },
        { "NZDUSD", "NZDUSDT" },
        { "AUDUSD", "AUDUSDT" },
        { "XAUUSD", "XAUUSDT" }
    };
    public DetectionRules DetectionRules { get; set; } = new();
}
