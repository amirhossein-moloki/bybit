using System.Collections.Generic;

namespace TradingBot.Application.Models;

public class EnglishRules : LanguageRules
{
    public EnglishRules()
    {
        LongKeywords = new List<string> { "LONG", "BUY", "BULLISH", "🟢" };
        ShortKeywords = new List<string> { "SHORT", "SELL", "BEARISH", "🔴" };
        PriceKeywords = new List<string> { "ENTRY", "BUY", "SELL", "PRICE", "TARGET" };
        RiskKeywords = new List<string> { "SL", "STOP LOSS", "TP", "TAKE PROFIT" };
    }
}
