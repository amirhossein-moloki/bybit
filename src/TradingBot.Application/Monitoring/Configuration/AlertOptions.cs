using System;
using System.Collections.Generic;

namespace TradingBot.Application.Monitoring.Configuration;

public class AlertOptions
{
    public bool Enabled { get; set; } = true;
    public AlertDeduplicationSettings Deduplication { get; set; } = new();
    public Dictionary<string, AlertRuleSettings> Rules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class AlertDeduplicationSettings
{
    public bool Enabled { get; set; } = true;
    public int WindowSeconds { get; set; } = 60;
}

public class AlertRuleSettings
{
    public bool Enabled { get; set; } = true;
    public string Severity { get; set; } = "WARNING";
    public string? Threshold { get; set; } // e.g., "30s"
    public string? Cooldown { get; set; } // e.g., "5m"
    public string? RepeatNotificationInterval { get; set; } // e.g., "10m"
    public string? Component { get; set; }
    public string? EventType { get; set; }

    public TimeSpan? GetThresholdTimeSpan()
    {
        return ParseTimeSpan(Threshold);
    }

    public TimeSpan? GetCooldownTimeSpan()
    {
        return ParseTimeSpan(Cooldown);
    }

    public TimeSpan? GetRepeatNotificationIntervalTimeSpan()
    {
        return ParseTimeSpan(RepeatNotificationInterval);
    }

    private static TimeSpan? ParseTimeSpan(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim().ToLowerInvariant();

        if (input.EndsWith("s"))
        {
            if (double.TryParse(input[..^1], out var seconds))
                return TimeSpan.FromSeconds(seconds);
        }
        else if (input.EndsWith("m"))
        {
            if (double.TryParse(input[..^1], out var minutes))
                return TimeSpan.FromMinutes(minutes);
        }
        else if (input.EndsWith("h"))
        {
            if (double.TryParse(input[..^1], out var hours))
                return TimeSpan.FromHours(hours);
        }
        else if (double.TryParse(input, out var totalSeconds))
        {
            return TimeSpan.FromSeconds(totalSeconds);
        }

        return null;
    }
}
