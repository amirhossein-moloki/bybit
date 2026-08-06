using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TradingBot.Telegram.Interfaces;
using TradingBot.Telegram.Models;

namespace TradingBot.Telegram.Health;

public class TelegramHealthCheck : IHealthCheck
{
    private readonly ITelegramClient _telegramClient;

    public TelegramHealthCheck(ITelegramClient telegramClient)
    {
        _telegramClient = telegramClient ?? throw new ArgumentNullException(nameof(telegramClient));
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var state = _telegramClient.CurrentState;

        return state switch
        {
            TelegramConnectionState.Connected or TelegramConnectionState.Listening =>
                Task.FromResult(HealthCheckResult.Healthy(state == TelegramConnectionState.Listening
                    ? "Telegram connection is healthy and listening."
                    : "Telegram connection is healthy and connected.")),
            TelegramConnectionState.Connecting or TelegramConnectionState.Authenticating or TelegramConnectionState.Reconnecting =>
                Task.FromResult(HealthCheckResult.Degraded($"Telegram connection is in state: {state}.")),
            _ =>
                Task.FromResult(HealthCheckResult.Unhealthy($"Telegram connection is in unhealthy state: {state}."))
        };
    }
}
