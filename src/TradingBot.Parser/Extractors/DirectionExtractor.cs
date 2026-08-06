using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TradingBot.Domain.Enums;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;

namespace TradingBot.Parser.Extractors;

public class DirectionExtractor : ISignalExtractor
{
    public Task ExtractAsync(ParserContext context, ParsedSignal signal)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (signal == null) throw new ArgumentNullException(nameof(signal));

        var normalized = SignalTextNormalizer.Normalize(context.RawMessage);

        // Standalone or matched phrases
        if (Regex.IsMatch(normalized, @"\b(LONG\s+POSITION|LONG|BUY|BULLISH)\b"))
        {
            signal.Side = OrderSide.Buy;
        }
        else if (Regex.IsMatch(normalized, @"\b(SHORT\s+POSITION|SHORT|SELL|BEARISH)\b"))
        {
            signal.Side = OrderSide.Sell;
        }

        return Task.CompletedTask;
    }
}
