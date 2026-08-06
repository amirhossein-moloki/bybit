using System.Collections.Generic;

namespace TradingBot.Application.Models;

public abstract class LanguageRules
{
    public List<string> LongKeywords { get; set; } = new();
    public List<string> ShortKeywords { get; set; } = new();
    public List<string> PriceKeywords { get; set; } = new();
    public List<string> RiskKeywords { get; set; } = new();
}
