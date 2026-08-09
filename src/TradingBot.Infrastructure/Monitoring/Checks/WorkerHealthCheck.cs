using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Monitoring;
using TradingBot.Application.Monitoring.Configuration;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;

namespace TradingBot.Infrastructure.Monitoring.Checks;

public class WorkerHealthCheck : IHealthCheck
{
    private readonly IWorkerHealthRegistry _registry;
    private readonly MonitoringOptions _options;

    public string Name => "Workers";

    public WorkerHealthCheck(IWorkerHealthRegistry registry, MonitoringOptions options)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        var heartbeats = _registry.GetWorkerHeartbeats();
        var now = DateTime.UtcNow;
        var staleThreshold = TimeSpan.FromSeconds(_options.Workers.StaleThresholdSeconds);

        var overallStatus = HealthStatus.Healthy;
        var isAnyStale = false;
        var isAnyFailed = false;

        foreach (var hb in heartbeats.Values)
        {
            var timeSinceHeartbeat = now - hb.LastHeartbeatAt;
            var isStale = timeSinceHeartbeat > staleThreshold;

            if (isStale)
            {
                isAnyStale = true;
                if (hb.IsCritical)
                {
                    overallStatus = HealthStatus.Unhealthy;
                }
                else if (overallStatus == HealthStatus.Healthy)
                {
                    overallStatus = HealthStatus.Degraded;
                }
            }

            if (string.Equals(hb.Status, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                isAnyFailed = true;
                if (hb.IsCritical)
                {
                    overallStatus = HealthStatus.Unhealthy;
                }
                else if (overallStatus == HealthStatus.Healthy)
                {
                    overallStatus = HealthStatus.Degraded;
                }
            }
        }

        var metaDict = new System.Collections.Generic.Dictionary<string, object>();
        foreach (var hb in heartbeats.Values)
        {
            var isStale = (now - hb.LastHeartbeatAt) > staleThreshold;
            metaDict[hb.WorkerName] = new
            {
                hb.Status,
                SecondsSinceLastHeartbeat = (now - hb.LastHeartbeatAt).TotalSeconds,
                IsStale = isStale,
                hb.IsCritical
            };
        }

        var metadata = JsonSerializer.Serialize(metaDict);

        var errMsg = isAnyStale ? "One or more background workers missed heartbeat." : null;
        if (isAnyFailed)
        {
            errMsg = (errMsg == null) ? "One or more background workers failed." : errMsg + " One or more background workers failed.";
        }

        return Task.FromResult(new HealthCheckResult(
            Name,
            overallStatus,
            DateTime.UtcNow,
            0,
            errorCode: (isAnyStale || isAnyFailed) ? "WORKER_ALERT" : null,
            errorMessage: errMsg,
            metadata: metadata
        ));
    }
}
