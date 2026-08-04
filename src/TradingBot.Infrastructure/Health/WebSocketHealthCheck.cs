using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TradingBot.Application.Enums;
using TradingBot.Application.Interfaces.Streams;

namespace TradingBot.Infrastructure.Health;

public class WebSocketHealthCheck : IHealthCheck
{
    private readonly IExchangeStreamClient _streamClient;

    public WebSocketHealthCheck(IExchangeStreamClient streamClient)
    {
        _streamClient = streamClient ?? throw new ArgumentNullException(nameof(streamClient));
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var state = _streamClient.State;

        return state switch
        {
            ConnectionState.Connected => Task.FromResult(HealthCheckResult.Healthy("WebSocket connection is established and healthy.")),
            ConnectionState.Connecting or ConnectionState.Reconnecting => Task.FromResult(HealthCheckResult.Degraded($"WebSocket connection is in {state} state.")),
            _ => Task.FromResult(HealthCheckResult.Unhealthy($"WebSocket connection is unhealthy. Current state: {state}"))
        };
    }
}
