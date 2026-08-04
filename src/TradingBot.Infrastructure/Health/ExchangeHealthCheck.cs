using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TradingBot.Infrastructure.Health;

public class ExchangeHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        // Placeholder check: In a real implementation, we would ping Bybit API or similar.
        // For Stage 01, we return healthy.
        return Task.FromResult(HealthCheckResult.Healthy("Exchange connectivity is functional (Placeholder)."));
    }
}
