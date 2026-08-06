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

    public static string PreparePattern(string rawPattern)
    {
        if (string.IsNullOrWhiteSpace(rawPattern)) return string.Empty;

        var pattern = rawPattern.Trim(':');
        // If it looks like a regex already (contains metacharacters like |, \, (, [, *, +), do not escape it.
        if (pattern.Contains('|') || pattern.Contains('\\') || pattern.Contains('(') || pattern.Contains('[') || pattern.Contains('*') || pattern.Contains('+'))
        {
            return pattern;
        }

        return Regex.Escape(pattern);
    }
}
