using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Monitoring.Configuration;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Monitoring;

public class HealthCheckEngine : IHealthCheckEngine
{
    private readonly IEnumerable<IHealthCheck> _healthChecks;
    private readonly MonitoringOptions _options;
    private readonly ILogger<HealthCheckEngine> _logger;

    // Concurrency protection across scopes
    private static readonly ConcurrentDictionary<string, byte> _runningChecks = new(StringComparer.OrdinalIgnoreCase);

    // Track last execution time for interval-based checks
    private static readonly ConcurrentDictionary<string, DateTime> _lastRunTimes = new(StringComparer.OrdinalIgnoreCase);

    public static void ResetState()
    {
        _runningChecks.Clear();
        _lastRunTimes.Clear();
    }

    public HealthCheckEngine(
        IEnumerable<IHealthCheck> healthChecks,
        MonitoringOptions options,
        ILogger<HealthCheckEngine> logger)
    {
        _healthChecks = healthChecks ?? throw new ArgumentNullException(nameof(healthChecks));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IEnumerable<HealthCheckResult>> RunAllChecksAsync(CancellationToken cancellationToken)
    {
        var results = new List<HealthCheckResult>();
        var now = DateTime.UtcNow;

        foreach (var check in _healthChecks)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            // Enforce configuration-based execution intervals
            var intervalSeconds = GetIntervalForCheck(check.Name);
            if (_lastRunTimes.TryGetValue(check.Name, out var lastRun))
            {
                if (now - lastRun < TimeSpan.FromSeconds(intervalSeconds))
                {
                    // Skip execution, interval has not elapsed yet
                    _logger.LogDebug("HealthCheckEngine: Skipping check '{CheckName}' because configured interval of {Interval}s has not elapsed.", check.Name, intervalSeconds);
                    continue;
                }
            }

            // Guard against overlap of the same check running concurrently
            if (!_runningChecks.TryAdd(check.Name, 0))
            {
                _logger.LogWarning("HealthCheckEngine: Check '{CheckName}' is already running. Skipping overlap.", check.Name);
                continue;
            }

            try
            {
                _lastRunTimes[check.Name] = now;
                var stopwatch = Stopwatch.StartNew();
                _logger.LogDebug("HealthCheckEngine: Starting check '{CheckName}'...", check.Name);

                // Enforce specific timeout via a linked cancellation token source
                var timeoutSeconds = GetTimeoutForCheck(check.Name);

                HealthCheckResult result;
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                    try
                    {
                        result = await check.CheckAsync(cts.Token);
                    }
                    catch (OperationCanceledException) when (cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
                    {
                        stopwatch.Stop();
                        _logger.LogError("HealthCheckEngine: Check '{CheckName}' timed out after {Timeout} seconds.", check.Name, timeoutSeconds);
                        result = new HealthCheckResult(
                            check.Name,
                            HealthStatus.Unhealthy,
                            DateTime.UtcNow,
                            stopwatch.ElapsedMilliseconds,
                            errorCode: "Timeout",
                            errorMessage: $"Health check timed out after {timeoutSeconds} seconds."
                        );
                    }
                    catch (Exception ex)
                    {
                        stopwatch.Stop();
                        var sanitizedMsg = Sanitize(ex.Message);
                        _logger.LogError(ex, "HealthCheckEngine: Check '{CheckName}' failed with exception.", check.Name);
                        result = new HealthCheckResult(
                            check.Name,
                            HealthStatus.Unhealthy,
                            DateTime.UtcNow,
                            stopwatch.ElapsedMilliseconds,
                            errorCode: ex.GetType().Name,
                            errorMessage: sanitizedMsg
                        );
                    }
                }

                stopwatch.Stop();
                results.Add(result);
            }
            finally
            {
                _runningChecks.TryRemove(check.Name, out _);
            }
        }

        return results;
    }

    private int GetIntervalForCheck(string checkName)
    {
        return checkName.ToLowerInvariant() switch
        {
            "database" => _options.Database.IntervalSeconds,
            "bybit rest" or "bybitrest" => _options.BybitRest.IntervalSeconds,
            "bybit websocket" or "bybitwebsocket" => _options.BybitWebSocket.IntervalSeconds,
            "telegram" => _options.Telegram.IntervalSeconds,
            "workers" => _options.Workers.IntervalSeconds,
            _ => 30 // Default fallback interval
        };
    }

    private int GetTimeoutForCheck(string checkName)
    {
        return checkName.ToLowerInvariant() switch
        {
            "database" => _options.Database.TimeoutSeconds,
            "bybit rest" or "bybitrest" => _options.BybitRest.TimeoutSeconds,
            "telegram" => _options.Telegram.TimeoutSeconds,
            _ => 10 // Default fallback timeout
        };
    }

    private static string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        // 1. Redact key-value pairs like key: value or key=value to hide the actual credential values
        var keyValuePattern = @"(secret_key|api_key|apikey|secret|password|token|sign|sign-type)(\s*[:=]\s*)([^\s,|]+)";
        var sanitized = System.Text.RegularExpressions.Regex.Replace(
            input,
            keyValuePattern,
            "$1$2[REDACTED]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // 2. Also redact any remaining standalone sensitive words to be absolutely secure
        var sensitivePatterns = new[] { "secret_key", "api_key", "apikey", "secret", "password", "token", "sign", "sign-type" };
        foreach (var pattern in sensitivePatterns)
        {
            sanitized = System.Text.RegularExpressions.Regex.Replace(
                sanitized,
                @"\b" + pattern + @"\b",
                "[REDACTED]",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        return sanitized;
    }
}
