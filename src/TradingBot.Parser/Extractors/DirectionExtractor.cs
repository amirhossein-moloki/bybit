using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TradingBot.Domain.Enums;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;
using TradingBot.Parser.Templates;

namespace TradingBot.Parser.Extractors;

public class DirectionExtractor : ISignalExtractor
{
    public Task ExtractAsync(ParserContext context, ParsedSignal signal)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (signal == null) throw new ArgumentNullException(nameof(signal));

        var normalized = SignalTextNormalizer.Normalize(context.RawMessage);

        // 1. Check for custom template pattern
        var activeTemplate = TemplateContext.Current;
        TemplateRule? rule = null;
        if (activeTemplate != null)
        {
            rule = activeTemplate.GetRules().FirstOrDefault(r => r.Extractor == "DirectionExtractor" || r.Field == "Side");
        }

        if (rule != null && !string.IsNullOrWhiteSpace(rule.Pattern))
        {
            var pattern = rule.Pattern;
            if (Regex.IsMatch(normalized, $@"\b{Regex.Escape(pattern)}\b", RegexOptions.IgnoreCase))
            {
                if (Regex.IsMatch(pattern, @"\b(LONG|BUY|BULLISH)\b", RegexOptions.IgnoreCase))
                {
                    signal.Side = OrderSide.Buy;
                    return Task.CompletedTask;
                }
                else if (Regex.IsMatch(pattern, @"\b(SHORT|SELL|BEARISH)\b", RegexOptions.IgnoreCase))
                {
                    signal.Side = OrderSide.Sell;
                    return Task.CompletedTask;
                }
            }
        }

        // Standalone or matched phrases (Fallback/Default patterns)
        if (Regex.IsMatch(normalized, @"\b(LONG\s+POSITION|LONG|BUY|BULLISH)\b", RegexOptions.IgnoreCase))
        {
            signal.Side = OrderSide.Buy;
        }
        else if (Regex.IsMatch(normalized, @"\b(SHORT\s+POSITION|SHORT|SELL|BEARISH)\b", RegexOptions.IgnoreCase))
        {
            signal.Side = OrderSide.Sell;
        }

        return Task.CompletedTask;
    }
}
