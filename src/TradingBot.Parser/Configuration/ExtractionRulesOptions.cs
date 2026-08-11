using System.Collections.Generic;

namespace TradingBot.Parser.Configuration;

public class SymbolRules
{
    public List<string> AllowedSymbols { get; set; } = new()
    {
        "BTCUSDT", "ETHUSDT", "SOLUSDT", "XRPUSDT", "EURUSD", "GBPUSD", "XAUUSD", "GOLD", "BTC", "ETH", "SOL", "XRP", "LTC"
    };

    public Dictionary<string, string> SymbolMappings { get; set; } = new()
    {
        { "GOLD", "XAUUSD" },
        { "BTC", "BTCUSDT" },
        { "ETH", "ETHUSDT" },
        { "SOL", "SOLUSDT" },
        { "XRP", "XRPUSDT" },
        { "LTC", "LTCUSDT" }
    };
}

public class SideRules
{
    public List<string> BuyKeywords { get; set; } = new() { "BUY", "LONG", "شراء", "لانگ" };
    public List<string> SellKeywords { get; set; } = new() { "SELL", "SHORT", "فروش", "شورت" };
}

public class EntryRules
{
    public List<string> EntryKeywords { get; set; } = new() { "ENTRY", "OPEN", "ورود", "ENTRY ZONE" };
}

public class SLRules
{
    public List<string> StopLossKeywords { get; set; } = new() { "SL", "STOP LOSS", "STOPLOSS", "استاپ", "حد ضرر" };
}

public class TPRules
{
    public List<string> TakeProfitKeywords { get; set; } = new() { "TP", "TARGET", "هدف", "حد سود" };
}

public class ExtractionRulesOptions
{
    public static string SectionName => "ExtractionRules";
    public SymbolRules SymbolRules { get; set; } = new();
    public SideRules SideRules { get; set; } = new();
    public EntryRules EntryRules { get; set; } = new();
    public SLRules SLRules { get; set; } = new();
    public TPRules TPRules { get; set; } = new();
}
