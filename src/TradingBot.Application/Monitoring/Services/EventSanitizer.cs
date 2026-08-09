using System;
using System.Text.RegularExpressions;

namespace TradingBot.Application.Monitoring.Services;

public class EventSanitizer : IEventSanitizer
{
    private static readonly Regex KeyValuePattern = new(
        @"(secret_key|api_key|apikey|secret|password|token|auth|authorization|bearer)(\s*[:=]\s*)([^\s,|]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BearerPattern = new(
        @"bearer\s+([^\s,|]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] SensitivePatterns = new[]
    {
        "secret_key", "api_key", "apikey", "secret", "password", "token", "auth", "authorization", "bearer"
    };

    public string? Sanitize(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input;

        // 1. Redact bearer token values first (e.g. Bearer tokenValue)
        var sanitized = BearerPattern.Replace(input, "bearer [REDACTED]");

        // 2. Redact key-value pairs like key: value or key=value to hide the actual credential values
        sanitized = KeyValuePattern.Replace(sanitized, "$1$2[REDACTED]");

        // 3. Also redact any remaining standalone sensitive words to be absolutely secure
        foreach (var pattern in SensitivePatterns)
        {
            sanitized = Regex.Replace(
                sanitized,
                @"\b" + pattern + @"\b",
                "[REDACTED]",
                RegexOptions.IgnoreCase);
        }

        return sanitized;
    }

    public string? SanitizeAndLimit(string? input, int maxLength)
    {
        if (string.IsNullOrEmpty(input)) return input;

        var sanitized = Sanitize(input);

        if (sanitized != null && sanitized.Length > maxLength)
        {
            // Truncate or Summarize cleanly (Section 11)
            sanitized = sanitized[..maxLength] + "... [TRUNCATED]";
        }

        return sanitized;
    }
}
