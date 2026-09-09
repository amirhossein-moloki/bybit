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

        string patternToUse = @"(?:STOP\s+LOSS|STOPLOSS|\bSL\b|حد\s*ضرر)";
        if (rule != null && !string.IsNullOrWhiteSpace(rule.Pattern))
        {
            var preparedPattern = SignalTextNormalizer.PreparePattern(rule.Pattern);
            patternToUse = $"(?:{preparedPattern})";
        }

        var match = Regex.Match(normalized, patternToUse + @"(?:\s*\([^)]*\))?[\s:]*([0-9.,]+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var val = match.Groups[1].Value;
            var cleanNum = val.Replace(",", "");
            if (decimal.TryParse(cleanNum, out var sl))
            {
                signal.StopLoss = sl;
            }
            else
            {
                signal.Errors.Add("Invalid stop loss format");
            }
        }
        else if (Regex.IsMatch(normalized, patternToUse + @"[\s:]*\S+", RegexOptions.IgnoreCase))
        {
            signal.Errors.Add("Invalid stop loss format");
        }

        return Task.CompletedTask;
    }
}
