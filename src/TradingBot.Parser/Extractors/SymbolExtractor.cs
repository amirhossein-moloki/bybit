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

        // 1. Check for explicit currency pairs with separator or standard quote currency (USDT, USDC, BUSD, USD)
        var pairMatch = Regex.Match(normalized, @"(?:جفت\s*ارز|PAIR|SYMBOL)?[\s:]*\b([A-Z]{2,6})[-/_]([A-Z]{3,4})\b", RegexOptions.IgnoreCase);
        if (!pairMatch.Success)
        {
            pairMatch = Regex.Match(normalized, @"(?:جفت\s*ارز|PAIR|SYMBOL)?[\s:]*\b([A-Z]{2,6})(USDT|USDC|BUSD|USD)\b", RegexOptions.IgnoreCase);
        }

        if (pairMatch.Success)
        {
            var baseSymbol = pairMatch.Groups[1].Value.ToUpperInvariant();
            var quoteSymbol = pairMatch.Groups[2].Value.ToUpperInvariant();

            if (!ExcludedWords.Contains(baseSymbol) && !ExcludedWords.Contains(quoteSymbol))
            {
                if (quoteSymbol is "USD" or "USDC" or "BUSD")
                {
                    signal.Symbol = $"{baseSymbol}USDT";
                }
                else
                {
                    signal.Symbol = $"{baseSymbol}{quoteSymbol}";
                }
                return Task.CompletedTask;
            }
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
