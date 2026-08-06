using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;
using TradingBot.Parser.Templates;

namespace TradingBot.Parser.Extractors;

public class TakeProfitExtractor : ISignalExtractor
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
            rule = activeTemplate.GetRules().FirstOrDefault(r => r.Extractor == "TakeProfitExtractor" || r.Field == "TakeProfits" || r.Field == "TakeProfit");
        }

        string patternToUse = @"\b(TP\d*|TARGET\d*)\b";
        if (rule != null && !string.IsNullOrWhiteSpace(rule.Pattern))
        {
            var preparedPattern = SignalTextNormalizer.PreparePattern(rule.Pattern);
            patternToUse = $@"\b({preparedPattern})\d*\b";
        }

        var matches = Regex.Matches(normalized, patternToUse + @"\s*[:\s-]*(\S+)", RegexOptions.IgnoreCase);
        foreach (Match match in matches)
        {
            var val = match.Groups[2].Value;
            var numMatch = Regex.Match(val, @"^([\d,]+(?:\.\d+)?)");
            if (numMatch.Success)
            {
                var cleanNum = numMatch.Groups[1].Value.Replace(",", "");
                if (decimal.TryParse(cleanNum, out var tp))
                {
                    if (!signal.TakeProfits.Contains(tp))
                    {
                        signal.TakeProfits.Add(tp);
                    }
                }
                else
                {
                    signal.Errors.Add("Invalid take profit format");
                }
            }
            else
            {
                signal.Errors.Add("Invalid take profit format");
            }
        }

        return Task.CompletedTask;
    }
}
