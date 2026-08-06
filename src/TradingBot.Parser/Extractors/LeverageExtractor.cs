using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;
using TradingBot.Parser.Templates;

namespace TradingBot.Parser.Extractors;

public class LeverageExtractor : ISignalExtractor
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
            rule = activeTemplate.GetRules().FirstOrDefault(r => r.Extractor == "LeverageExtractor" || r.Field == "Leverage");
        }

        if (rule != null && !string.IsNullOrWhiteSpace(rule.Pattern))
        {
            var escapedPattern = Regex.Escape(rule.Pattern.Trim(':'));
            var matchCustom = Regex.Match(normalized, $@"\b{escapedPattern}\s*[:\s-]*(-?\d+)\b", RegexOptions.IgnoreCase);
            if (matchCustom.Success)
            {
                if (int.TryParse(matchCustom.Groups[1].Value, out var lev) && lev > 0)
                {
                    signal.Leverage = lev;
                    return Task.CompletedTask;
                }
            }
        }

        // Pattern 1: Leverage:50
        var matchLeverage = Regex.Match(normalized, @"\bLEVERAGE\s*[:\s-]*(-?\d+)\b", RegexOptions.IgnoreCase);
        if (matchLeverage.Success)
        {
            if (int.TryParse(matchLeverage.Groups[1].Value, out var lev) && lev > 0)
            {
                signal.Leverage = lev;
                return Task.CompletedTask;
            }
        }

        // Pattern 2: 20X or 20x
        var matchX = Regex.Match(normalized, @"(-?\d+)\s*X\b", RegexOptions.IgnoreCase);
        if (matchX.Success)
        {
            if (int.TryParse(matchX.Groups[1].Value, out var lev) && lev > 0)
            {
                signal.Leverage = lev;
                return Task.CompletedTask;
            }
        }

        return Task.CompletedTask;
    }
}
