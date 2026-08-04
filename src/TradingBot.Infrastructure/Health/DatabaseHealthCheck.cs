using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TradingBot.Infrastructure.Health;

public class DatabaseHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // Placeholder check: In a real implementation, we would check DB connection.
        // For Stage 01, we return healthy.
        return Task.FromResult(HealthCheckResult.Healthy("Database is online (InMemory / Placeholder)."));
    }
}
