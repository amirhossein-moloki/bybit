using System;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Monitoring;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Telegram.Interfaces;
using TradingBot.Telegram.Models;

namespace TradingBot.Telegram.Health;

public class MonitoringTelegramHealthCheck : IHealthCheck
{
    private readonly ITelegramClient _telegramClient;

    public string Name => "Telegram";

    public MonitoringTelegramHealthCheck(ITelegramClient telegramClient)
    {
        _telegramClient = telegramClient ?? throw new ArgumentNullException(nameof(telegramClient));
    }

    public Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        var state = _telegramClient.CurrentState;

        var (status, message, connStatus) = state switch
        {
            TelegramConnectionState.Connected or TelegramConnectionState.Listening =>
                (HealthStatus.Healthy, "Telegram is connected and listener is active.", ConnectionStatus.Connected),
            TelegramConnectionState.Connecting or TelegramConnectionState.Authenticating or TelegramConnectionState.Reconnecting =>
                (HealthStatus.Degraded, $"Telegram connection transition state: {state}.", ConnectionStatus.Connecting),
            TelegramConnectionState.AuthenticationFailed =>
                (HealthStatus.Unhealthy, "Telegram authentication failed. Check credentials/session.", ConnectionStatus.Failed),
            _ =>
                (HealthStatus.Unhealthy, $"Telegram is in unhealthy state: {state}.", ConnectionStatus.Disconnected)
        };

        var metadata = $"{{\"ConnectionStatus\":\"{connStatus}\",\"RawState\":\"{state}\"}}";

        return Task.FromResult(new HealthCheckResult(
            Name,
            status,
            DateTime.UtcNow,
            0,
            errorCode: status == HealthStatus.Healthy ? null : state.ToString(),
            errorMessage: status == HealthStatus.Healthy ? null : message,
            metadata: metadata
        ));
    }
}
