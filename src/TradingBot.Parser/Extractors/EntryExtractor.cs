using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;
using TradingBot.Parser.Templates;

namespace TradingBot.Parser.Extractors;

public class EntryExtractor : ISignalExtractor
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
            rule = activeTemplate.GetRules().FirstOrDefault(r => r.Extractor == "EntryExtractor" || r.Field == "EntryPrice");
        }

        string patternToUse = @"(?:ENTRY|ENTRY\s+PRICE|BUY\s+ZONE|BUY|نقطه\s*ورود|ورود)";
        if (rule != null && !string.IsNullOrWhiteSpace(rule.Pattern))
        {
            var preparedPattern = SignalTextNormalizer.PreparePattern(rule.Pattern);
            patternToUse = $"(?:{preparedPattern})";
        }

        // Check for "NOW" suffix
        var nowPattern = rule != null && !string.IsNullOrWhiteSpace(rule.Pattern)
            ? $@"\b({SignalTextNormalizer.PreparePattern(rule.Pattern)})\s+NOW\b"
            : @"\b(ENTRY|BUY)\s+NOW\b";

        if (Regex.IsMatch(normalized, nowPattern, RegexOptions.IgnoreCase) || Regex.IsMatch(normalized, @"\b(ENTRY|BUY)\s+NOW\b", RegexOptions.IgnoreCase))
        {
            return Task.CompletedTask;
        }

        var match = Regex.Match(normalized, patternToUse + @"[\s:]*([0-9.,]+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var val = match.Groups[1].Value;
            var cleanNum = val.Replace(",", "");
            if (decimal.TryParse(cleanNum, out var price))
            {
                signal.EntryPrice = price;
            }
            else
            {
                signal.Errors.Add("Invalid entry price format");
            }
        }
        else if (Regex.IsMatch(normalized, patternToUse + @"[\s:]*\S+", RegexOptions.IgnoreCase))
        {
            signal.Errors.Add("Invalid entry price format");
        }

        return Task.CompletedTask;
    }
}
