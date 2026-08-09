using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Monitoring;

public class HealthStatusProvider : IHealthStatusProvider
{
    private readonly ConcurrentDictionary<string, HealthCheckResult> _statuses = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> CriticalServices = new(StringComparer.OrdinalIgnoreCase)
    {
        "Database",
        "Bybit REST",
        "Bybit WebSocket",
        "BybitRest",
        "BybitWebSocket"
    };

    public HealthStatus GetOverallStatus()
    {
        if (_statuses.IsEmpty)
        {
            return HealthStatus.Unknown;
        }

        var overall = HealthStatus.Healthy;
        var hasDegraded = false;
        var hasNonCriticalUnhealthy = false;

        foreach (var status in _statuses.Values)
        {
            var isCritical = CriticalServices.Contains(status.ServiceName);

            if (status.Status == HealthStatus.Unhealthy)
            {
                if (isCritical)
                {
                    // Any critical dependency Unhealthy -> Unhealthy
                    return HealthStatus.Unhealthy;
                }
                else
                {
                    hasNonCriticalUnhealthy = true;
                }
            }
            else if (status.Status == HealthStatus.Degraded)
            {
                hasDegraded = true;
            }
        }

        if (hasDegraded || hasNonCriticalUnhealthy)
        {
            return HealthStatus.Degraded;
        }

        return overall;
    }

    public IReadOnlyDictionary<string, HealthCheckResult> GetComponentStatuses()
    {
        return _statuses;
    }

    public HealthCheckResult? GetComponentStatus(string componentName)
    {
        return _statuses.TryGetValue(componentName, out var result) ? result : null;
    }

    public void UpdateStatus(string componentName, HealthCheckResult result)
    {
        if (string.IsNullOrWhiteSpace(componentName)) return;
        _statuses[componentName] = result ?? throw new ArgumentNullException(nameof(result));
    }
}
