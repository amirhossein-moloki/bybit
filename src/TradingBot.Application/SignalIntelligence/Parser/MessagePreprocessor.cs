using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using TradingBot.Application.SignalIntelligence.Contracts;

namespace TradingBot.Application.SignalIntelligence.Parser;

public class MessagePreprocessor : IMessagePreprocessor
{
    private static readonly Dictionary<char, char> DigitsAndCharsMap = new()
    {
        // Persian digits
        { '۰', '0' }, { '۱', '1' }, { '۲', '2' }, { '۳', '3' }, { '۴', '4' },
        { '۵', '5' }, { '۶', '6' }, { '۷', '7' }, { '۸', '8' }, { '۹', '9' },
        // Arabic digits
        { '٠', '0' }, { '١', '1' }, { '٢', '2' }, { '٣', '3' }, { '٤', '4' },
        { '٥', '5' }, { '٦', '6' }, { '٧', '7' }, { '٨', '8' }, { '٩', '9' },
        // Arabic kaf and ya
        { 'ي', 'ی' }, { 'ك', 'ک' }
    };

    public string Preprocess(string rawContent)
    {
        if (string.IsNullOrWhiteSpace(rawContent))
        {
            return string.Empty;
        }

        // 1. Normalize line endings to \n
        string text = rawContent.Replace("\r\n", "\n").Replace("\r", "\n");

        // 2. Map Persian/Arabic digits and characters
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (DigitsAndCharsMap.TryGetValue(c, out char mappedChar))
            {
                sb.Append(mappedChar);
            }
            else
            {
                sb.Append(c);
            }
        }
        text = sb.ToString();

        // 3. Normalize common separators: Standardize spaces around slashes '/', hyphens '-', colons ':'
        // e.g. "EUR / USD" -> "EUR/USD", " ورورد : " -> "ورود:"
        text = Regex.Replace(text, @"\s*([/:])\s*", "$1");
        // For hyphens, normalize space but be careful not to corrupt negative numbers or entry ranges.
        // Let's standardise separator hyphens between letters like "EUR-USD"
        text = Regex.Replace(text, @"([a-zA-Z])\s*-\s*([a-zA-Z])", "$1-$2");

        // 4. Normalize repeated whitespace (excluding newlines)
        text = Regex.Replace(text, @"[ \t]+", " ");

        // Trim spaces before and after newlines
        text = Regex.Replace(text, @"[ \t]*\n[ \t]*", "\n");

        // 5. Collapse repeated newlines
        text = Regex.Replace(text, @"\n+", "\n");

        return text.Trim();
    }
}
