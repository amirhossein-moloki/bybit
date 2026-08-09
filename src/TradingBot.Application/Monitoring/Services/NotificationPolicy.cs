using System;
using TradingBot.Application.Monitoring.Configuration;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Monitoring.Services;

public class NotificationPolicy : INotificationPolicy
{
    private readonly NotificationOptions _options;

    public NotificationPolicy(NotificationOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public bool ShouldNotify(MonitoringEvent @event)
    {
        if (Environment.GetEnvironmentVariable("NOTIFICATIONS_DISABLED") == "true")
        {
            return false;
        }

        if (!_options.Enabled)
        {
            return false;
        }

        // Critical events are notified by default, unless explicitly disabled.
        if (@event.Severity == "CRITICAL" && _options.Events.CriticalError)
        {
            return true;
        }

        // Check if the event type is enabled in configuration
        return _options.Events.IsEnabled(@event.EventType);
    }
}
