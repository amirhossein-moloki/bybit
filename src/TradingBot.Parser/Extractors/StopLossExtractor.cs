using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;

namespace TradingBot.Parser.Extractors;

public class StopLossExtractor : ISignalExtractor
{
    public Task ExtractAsync(ParserContext context, ParsedSignal signal)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (signal == null) throw new ArgumentNullException(nameof(signal));

        var normalized = SignalTextNormalizer.Normalize(context.RawMessage);

        var match = Regex.Match(normalized, @"\b(STOP\s+LOSS|STOPLOSS|SL)\b\s*[:\s-]*(\S+)");
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
