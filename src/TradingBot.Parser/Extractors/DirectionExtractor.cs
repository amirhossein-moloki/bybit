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

        // Check explicit English BUY/SELL token priority first
        if (Regex.IsMatch(normalized, @"\bBUY\b", RegexOptions.IgnoreCase) && !Regex.IsMatch(normalized, @"\bSELL\b", RegexOptions.IgnoreCase))
        {
            signal.Side = OrderSide.Buy;
            return Task.CompletedTask;
        }
        if (Regex.IsMatch(normalized, @"\bSELL\b", RegexOptions.IgnoreCase) && !Regex.IsMatch(normalized, @"\bBUY\b", RegexOptions.IgnoreCase))
        {
            signal.Side = OrderSide.Sell;
            return Task.CompletedTask;
        }

        // Standalone or matched phrases (Fallback/Default patterns including Persian labels)
        if (Regex.IsMatch(normalized, @"(?:نوع\s*معامله[\s:]*خرید|\bLONG\s+POSITION\b|\bLONG\b|\bBUY\b|\bBULLISH\b|خرید)", RegexOptions.IgnoreCase))
        {
            signal.Side = OrderSide.Buy;
        }
        else if (Regex.IsMatch(normalized, @"(?:نوع\s*معامله[\s:]*فروش|\bSHORT\s+POSITION\b|\bSHORT\b|\bSELL\b|\bBEARISH\b|فروش)", RegexOptions.IgnoreCase))
        {
            signal.Side = OrderSide.Sell;
        }

        return Task.CompletedTask;
    }
}
