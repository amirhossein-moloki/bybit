using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;

namespace TradingBot.Parser.Extractors;

public class EntryExtractor : ISignalExtractor
{
    public Task ExtractAsync(ParserContext context, ParsedSignal signal)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (signal == null) throw new ArgumentNullException(nameof(signal));

        var normalized = SignalTextNormalizer.Normalize(context.RawMessage);

        // 1. Check for "ENTRY NOW" or "BUY NOW" - if found, we just return (no price, but also no format error)
        if (Regex.IsMatch(normalized, @"\b(ENTRY|BUY)\s+NOW\b"))
        {
            return Task.CompletedTask;
        }

        // 2. Search for entry keywords followed by price
        // Supports: ENTRY, BUY ZONE, BUY
        var match = Regex.Match(normalized, @"\b(ENTRY|BUY\s+ZONE|BUY)\b\s*[:\s-]*(\S+)");
        if (match.Success)
        {
            var val = match.Groups[2].Value;
            var numMatch = Regex.Match(val, @"^([\d,]+(?:\.\d+)?)");
            if (numMatch.Success)
            {
                var cleanNum = numMatch.Groups[1].Value.Replace(",", "");
                if (decimal.TryParse(cleanNum, out var price))
                {
                    signal.EntryPrice = price;
                }
                else
                {
                    signal.Errors.Add("Invalid entry price format");
                }
            }
            else
            {
                // If it's not a number (like "ABC"), it's an extraction error
                signal.Errors.Add("Invalid entry price format");
            }
        }

        return Task.CompletedTask;
    }
}
