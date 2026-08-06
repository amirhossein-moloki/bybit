using System;
using System.Text.RegularExpressions;

namespace TradingBot.Parser;

public static class SignalTextNormalizer
{
    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        // Remove emojis and other special symbols
        // \p{Cs} (surrogates), \p{Co} (private use), \p{Cn} (unassigned), \p{So} (other symbols/emojis)
        var noEmojis = Regex.Replace(text, @"\p{Cs}|\p{Co}|\p{Cn}|\p{So}", " ");

        // Normalize line breaks to \n
        var normalizedBreaks = noEmojis.Replace("\r\n", "\n").Replace("\r", "\n");

        // Collapse multiple spaces within lines (excluding newlines)
        var collapsedSpaces = Regex.Replace(normalizedBreaks, @"[ \t]+", " ");

        // Collapse multiple newlines
        var collapsedNewlines = Regex.Replace(collapsedSpaces, @"\n+", "\n");

        // Convert to uppercase and trim
        return collapsedNewlines.Trim().ToUpperInvariant();
    }
}
