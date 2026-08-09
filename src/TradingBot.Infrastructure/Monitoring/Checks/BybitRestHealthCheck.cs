using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Monitoring;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;

namespace TradingBot.Infrastructure.Monitoring.Checks;

public class BybitRestHealthCheck : IHealthCheck
{
    private readonly IExchangeClient _exchangeClient;

    public string Name => "Bybit REST";

    public BybitRestHealthCheck(IExchangeClient _client)
    {
        _exchangeClient = _client ?? throw new ArgumentNullException(nameof(_client));
    }

    public async Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var pingSuccess = await _exchangeClient.PingAsync(cancellationToken);
            stopwatch.Stop();

            if (!pingSuccess)
            {
                return new HealthCheckResult(
                    Name,
                    HealthStatus.Unhealthy,
                    DateTime.UtcNow,
                    stopwatch.ElapsedMilliseconds,
                    errorCode: "PING_FAILED",
                    errorMessage: "Bybit API ping failed."
                );
            }

            try
            {
                var balance = await _exchangeClient.GetAccountBalanceAsync("USDT", cancellationToken);
                return new HealthCheckResult(
                    Name,
                    HealthStatus.Healthy,
                    DateTime.UtcNow,
                    stopwatch.ElapsedMilliseconds,
                    metadata: $"{{\"ResponseTimeMs\":{stopwatch.ElapsedMilliseconds},\"Authenticated\":true}}"
                );
            }
            catch (Exception authEx)
            {
                var errMessage = authEx.Message;
                var errorCode = "AUTHENTICATION_FAILED";

                if (errMessage.Contains("10003") || errMessage.Contains("10004") || errMessage.Contains("10005") ||
                    errMessage.Contains("API key", StringComparison.OrdinalIgnoreCase) ||
                    errMessage.Contains("signature", StringComparison.OrdinalIgnoreCase) ||
                    errMessage.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
                {
                    errorCode = "AuthenticationFailure";
                }
                else if (errMessage.Contains("Rate limit", StringComparison.OrdinalIgnoreCase) || errMessage.Contains("10018"))
                {
                    errorCode = "RateLimited";
                }
                else if (errMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase))
                {
                    errorCode = "Timeout";
                }
                else
                {
                    errorCode = "ExchangeFailure";
                }

                return new HealthCheckResult(
                    Name,
                    HealthStatus.Unhealthy,
                    DateTime.UtcNow,
                    stopwatch.ElapsedMilliseconds,
                    errorCode: errorCode,
                    errorMessage: $"Bybit REST private check failed: {errMessage}"
                );
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var errMessage = ex.Message;
            var errorCode = "NetworkFailure";

            if (errMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            {
                errorCode = "Timeout";
            }

            return new HealthCheckResult(
                Name,
                HealthStatus.Unhealthy,
                DateTime.UtcNow,
                stopwatch.ElapsedMilliseconds,
                errorCode: errorCode,
                errorMessage: $"Bybit REST connectivity failed: {errMessage}"
            );
        }
    }
}
