using System.Collections.Generic;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Monitoring;

public interface IHealthStatusProvider
{
    HealthStatus GetOverallStatus();
    IReadOnlyDictionary<string, HealthCheckResult> GetComponentStatuses();
    HealthCheckResult? GetComponentStatus(string componentName);
    void UpdateStatus(string componentName, HealthCheckResult result);
}
