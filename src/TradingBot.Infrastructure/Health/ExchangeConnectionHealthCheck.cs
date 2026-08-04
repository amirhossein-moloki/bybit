using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TradingBot.Application.Interfaces;

namespace TradingBot.Infrastructure.Health;

public class ExchangeConnectionHealthCheck : IHealthCheck
{
    private readonly IExchangeClient _exchangeClient;

    public ExchangeConnectionHealthCheck(IExchangeClient exchangeClient)
    {
        _exchangeClient = exchangeClient ?? throw new ArgumentNullException(nameof(exchangeClient));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var isConnected = await _exchangeClient.PingAsync(cancellationToken);
            if (isConnected)
            {
                return HealthCheckResult.Healthy("Exchange HTTP connection is functional.");
            }
            return HealthCheckResult.Degraded("Exchange HTTP ping failed.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Exchange HTTP connection failed with exception.", ex);
        }
    }
}
