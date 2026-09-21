using System.Collections.Generic;

namespace TradingBot.Parser.Configuration;

public class SymbolRules
{
    public List<string> AllowedSymbols { get; set; } = new()
    {
        "BTCUSDT", "ETHUSDT", "SOLUSDT", "XRPUSDT", "EURUSD", "GBPUSD", "AUDUSD", "NZDUSD", "USDCAD", "USDCHF", "USDJPY", "XAUUSD", "GOLD", "BTC", "ETH", "SOL", "XRP", "LTC"
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
    public List<string> BuyKeywords { get; set; } = new() { "BUY", "LONG", "خرید", "شراء", "لانگ" };
    public List<string> SellKeywords { get; set; } = new() { "SELL", "SHORT", "فروش", "شورت" };
}

public class EntryRules
{
    public List<string> EntryKeywords { get; set; } = new() { "نقطه ورود", "نقطه ورود:", "قیمت ورود", "قیمت ورود:", "ورود", "ورود:", "ENTRY", "ENTRY:", "OPEN", "ENTRY ZONE" };
}

public class SLRules
{
    public List<string> StopLossKeywords { get; set; } = new() { "حد ضرر", "حد ضرر (Stop Loss)", "حد ضرر:", "STOP LOSS", "STOPLOSS", "SL", "استاپ" };
}

public class TPRules
{
    public List<string> TakeProfitKeywords { get; set; } = new() { "تارگت اول", "تارگت دوم", "تارگت سوم", "تارگت", "حد سود", "TP", "TARGET", "هدف" };
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
