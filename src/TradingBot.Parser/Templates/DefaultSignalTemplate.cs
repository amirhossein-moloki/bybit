using System.Collections.Generic;
using TradingBot.Parser.Models;

namespace TradingBot.Parser.Templates;

public class DefaultSignalTemplate : ISignalTemplate
{
    private static readonly List<TemplateRule> DefaultRules = new()
    {
        new TemplateRule { Field = "Symbol", Pattern = "", Extractor = "SymbolExtractor", Required = true, Order = 1 },
        new TemplateRule { Field = "Side", Pattern = "", Extractor = "DirectionExtractor", Required = true, Order = 2 },
        new TemplateRule { Field = "EntryPrice", Pattern = "ENTRY|BUY\\s+ZONE|BUY", Extractor = "EntryExtractor", Required = false, Order = 3 },
        new TemplateRule { Field = "StopLoss", Pattern = "STOP\\s+LOSS|STOPLOSS|SL", Extractor = "StopLossExtractor", Required = false, Order = 4 },
        new TemplateRule { Field = "TakeProfits", Pattern = "TP|TARGET", Extractor = "TakeProfitExtractor", Required = false, Order = 5 },
        new TemplateRule { Field = "Leverage", Pattern = "LEVERAGE", Extractor = "LeverageExtractor", Required = false, Order = 6 }
    };

    public bool CanHandle(ParserContext context)
    {
        return true; // Fallback handles anything
    }

    public IReadOnlyList<TemplateRule> GetRules()
    {
        return DefaultRules;
    }
}
