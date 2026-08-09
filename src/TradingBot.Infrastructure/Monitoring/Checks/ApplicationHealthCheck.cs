using System;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Monitoring;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;

namespace TradingBot.Infrastructure.Monitoring.Checks;

public class ApplicationHealthCheck : IHealthCheck
{
    public string Name => "Application";

    public Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(new HealthCheckResult(
            Name,
            HealthStatus.Healthy,
            DateTime.UtcNow,
            0,
            metadata: "{\"Process\":\"Alive\",\"Lifecycle\":\"Valid\"}"
        ));
    }
}
