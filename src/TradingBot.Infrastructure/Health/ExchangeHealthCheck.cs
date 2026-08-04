using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TradingBot.Application.Interfaces;

namespace TradingBot.Infrastructure.Health;

public class ExchangeHealthCheck : IHealthCheck
{
    private readonly IExchangeClient _exchangeClient;

    public ExchangeHealthCheck(IExchangeClient exchangeClient)
    {
        _exchangeClient = exchangeClient ?? throw new ArgumentNullException(nameof(exchangeClient));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var isHealthy = await _exchangeClient.PingAsync(cancellationToken);
            if (isHealthy)
            {
                return HealthCheckResult.Healthy($"Exchange {_exchangeClient.ExchangeName} connectivity is functional.");
            }

            return HealthCheckResult.Unhealthy($"Exchange {_exchangeClient.ExchangeName} connectivity check failed (Ping returned false).");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Exchange {_exchangeClient.ExchangeName} connectivity check failed with exception.", ex);
        }
    }
}
