using System;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Enums;
using TradingBot.Application.Interfaces.Streams;
using TradingBot.Application.Monitoring;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;

namespace TradingBot.Infrastructure.Monitoring.Checks;

public class BybitWebSocketHealthCheck : IHealthCheck
{
    private readonly IExchangeStreamClient _streamClient;

    public string Name => "Bybit WebSocket";

    public BybitWebSocketHealthCheck(IExchangeStreamClient streamClient)
    {
        _streamClient = streamClient ?? throw new ArgumentNullException(nameof(streamClient));
    }

    public Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        var state = _streamClient.State;

        var (status, message, connStatus) = state switch
        {
            ConnectionState.Connected =>
                (HealthStatus.Healthy, "Bybit WebSocket connection is healthy and connected.", ConnectionStatus.Connected),
            ConnectionState.Connecting =>
                (HealthStatus.Degraded, "Bybit WebSocket connection is currently connecting.", ConnectionStatus.Connecting),
            ConnectionState.Reconnecting =>
                (HealthStatus.Degraded, "Bybit WebSocket connection is currently reconnecting.", ConnectionStatus.Reconnecting),
            ConnectionState.Failed =>
                (HealthStatus.Unhealthy, "Bybit WebSocket connection failed.", ConnectionStatus.Failed),
            _ =>
                (HealthStatus.Unhealthy, $"Bybit WebSocket connection is in state: {state}", ConnectionStatus.Disconnected)
        };

        var metadata = $"{{\"ConnectionStatus\":\"{connStatus}\",\"RawState\":\"{state}\"}}";

        return Task.FromResult(new HealthCheckResult(
            Name,
            status,
            DateTime.UtcNow,
            0,
            errorMessage: status == HealthStatus.Healthy ? null : message,
            metadata: metadata
        ));
    }
}
