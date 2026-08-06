using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;

namespace TradingBot.Parser.Extractors;

public class SymbolExtractor : ISignalExtractor
{
    private static readonly HashSet<string> ExcludedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "LONG", "SHORT", "BUY", "SELL", "ENTRY", "STOP", "LOSS", "TAKE", "PROFIT", "LEVERAGE",
        "ZONE", "TARGET", "LIMIT", "MARKET", "NOW", "SL", "TP", "HIGH", "LOW", "RISK", "RISKY",
        "CROSS", "ISOLATED", "CALL", "SIGNAL", "TRADE", "POSITION", "BULLISH", "BEARISH", "WARN",
        "WARNING", "ERROR", "EXCHANGE", "PRICE", "PRICES"
    };

    public Task ExtractAsync(ParserContext context, ParsedSignal signal)
    {
        if (context == null) throw new ArgumentNullException(nameof(context));
        if (signal == null) throw new ArgumentNullException(nameof(signal));

        var normalized = SignalTextNormalizer.Normalize(context.RawMessage);

        // 1. Check for explicit USDT/USDC/BUSD pairs with optional separators
        var explicitPairMatch = Regex.Match(normalized, @"\b([A-Z0-9]{2,10})[-/_]?(USDT|USDC|BUSD)\b");
        if (explicitPairMatch.Success)
        {
            var baseSymbol = explicitPairMatch.Groups[1].Value;
            // Normalize to USDT standard for Bybit
            signal.Symbol = $"{baseSymbol}USDT";
            return Task.CompletedTask;
        }

        // 2. Look for any words in the text that are non-numeric, not excluded, and 2-10 chars long
        var words = normalized.Split(new[] { ' ', '\n', '\t', ':', '-', '/', '_', ',', '.' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            if (word.Length >= 2 && word.Length <= 10 && !ExcludedWords.Contains(word) && !word.All(char.IsDigit))
            {
                signal.Symbol = $"{word}USDT";
                return Task.CompletedTask;
            }
        }

        return Task.CompletedTask;
    }
}
