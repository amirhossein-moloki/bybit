using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;
using TradingBot.Parser.Templates;

namespace TradingBot.Parser.Extractors;

public class StopLossExtractor : ISignalExtractor
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
            rule = activeTemplate.GetRules().FirstOrDefault(r => r.Extractor == "StopLossExtractor" || r.Field == "StopLoss");
        }

        string patternToUse = @"\b(STOP\s+LOSS|STOPLOSS|SL)\b";
        if (rule != null && !string.IsNullOrWhiteSpace(rule.Pattern))
        {
            var preparedPattern = SignalTextNormalizer.PreparePattern(rule.Pattern);
            patternToUse = $@"\b({preparedPattern})\b";
        }

        var match = Regex.Match(normalized, patternToUse + @"\s*[:\s-]*(\S+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var val = match.Groups[2].Value;
            var numMatch = Regex.Match(val, @"^([\d,]+(?:\.\d+)?)");
            if (numMatch.Success)
            {
                var cleanNum = numMatch.Groups[1].Value.Replace(",", "");
                if (decimal.TryParse(cleanNum, out var sl))
                {
                    signal.StopLoss = sl;
                }
                else
                {
                    signal.Errors.Add("Invalid stop loss format");
                }
            }
            else
            {
                signal.Errors.Add("Invalid stop loss format");
            }
        }

        return Task.CompletedTask;
    }
}
