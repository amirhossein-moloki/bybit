using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;

namespace TradingBot.Parser.Extractors;

public class LeverageExtractor : ISignalExtractor
{
    public Task ExtractAsync(ParserContext context, ParsedSignal signal)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (signal == null) throw new ArgumentNullException(nameof(signal));

        var normalized = SignalTextNormalizer.Normalize(context.RawMessage);

        // Pattern 1: Leverage:50
        var matchLeverage = Regex.Match(normalized, @"\bLEVERAGE\s*[:\s-]*(-?\d+)\b");
        if (matchLeverage.Success)
        {
            if (int.TryParse(matchLeverage.Groups[1].Value, out var lev) && lev > 0)
            {
                signal.Leverage = lev;
                return Task.CompletedTask;
            }
        }

        // Pattern 2: 20X or 20x
        var matchX = Regex.Match(normalized, @"(-?\d+)\s*X\b");
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
