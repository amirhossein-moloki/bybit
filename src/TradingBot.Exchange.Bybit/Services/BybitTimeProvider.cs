using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Interfaces;
using TradingBot.Exchange.Bybit.Dtos;

namespace TradingBot.Exchange.Bybit.Services;

/// <summary>
/// Provides internal time synchronization for Bybit API calls by computing and maintaining the offset between local system clock and Bybit server time.
/// </summary>
public class BybitTimeProvider : IExchangeTimeProvider, IHostedService, IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BybitSettings _settings;
    private readonly ILogger<BybitTimeProvider> _logger;

    private long _offsetMilliseconds;
    private CancellationTokenSource? _cts;
    private Task? _timerTask;

    public BybitTimeProvider(
        IHttpClientFactory httpClientFactory,
        BybitSettings settings,
        ILogger<BybitTimeProvider> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the stored offset in milliseconds between Bybit server time and local UTC time (ServerTime - LocalTime).
    /// </summary>
    public long OffsetMilliseconds => Interlocked.Read(ref _offsetMilliseconds);

    /// <summary>
    /// Returns the current Unix timestamp in milliseconds synchronized with the Bybit server time offset.
    /// </summary>
    public long GetCurrentMilliseconds()
    {
        var localUtcNowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var offset = Interlocked.Read(ref _offsetMilliseconds);
        return localUtcNowMs + offset;
    }

    /// <summary>
    /// Performs time synchronization with Bybit REST endpoint /v5/market/time and updates the offset.
    /// </summary>
    public async Task SyncTimeAsync(CancellationToken cancellationToken = default)
    {
        var syncOptions = _settings.TimeSync ?? new BybitTimeSyncOptions();
        if (!syncOptions.Enabled)
        {
            _logger.LogDebug("Bybit time synchronization is disabled in configuration.");
            return;
        }

        var timeoutSeconds = syncOptions.RequestTimeoutSeconds > 0 ? syncOptions.RequestTimeoutSeconds : 5;
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var localStartMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        try
        {
            var client = _httpClientFactory.CreateClient("BybitTimeProvider");
            var baseUrl = BybitOptions.GetBaseUrl(_settings.Environment);
            var requestUrl = new Uri(new Uri(baseUrl), "/v5/market/time");

            var response = await client.GetFromJsonAsync<BybitResponse<BybitServerTime>>(requestUrl, linkedCts.Token);
            var localEndMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var localUtcMs = (localStartMs + localEndMs) / 2;

            if (response == null || response.RetCode != 0)
            {
                _logger.LogWarning("Bybit time synchronization failed. RetCode={RetCode}, Msg={RetMsg}. Using previous offset.",
                    response?.RetCode, response?.RetMsg);
                return;
            }

            long serverTimeMs = 0;
            if (response.Result != null && long.TryParse(response.Result.TimeNano, out var timeNano) && timeNano > 0)
            {
                serverTimeMs = timeNano / 1_000_000;
            }
            else if (response.Time > 0)
            {
                serverTimeMs = response.Time;
            }
            else if (response.Result != null && long.TryParse(response.Result.TimeSecond, out var timeSec) && timeSec > 0)
            {
                serverTimeMs = timeSec * 1000;
            }

            if (serverTimeMs <= 0)
            {
                _logger.LogWarning("Bybit time synchronization failed: unable to parse server timestamp. Using previous offset.");
                return;
            }

            var newOffset = serverTimeMs - localUtcMs;
            Interlocked.Exchange(ref _offsetMilliseconds, newOffset);

            _logger.LogInformation("Bybit time synchronization completed.\nLocalTime: {LocalTime}\nServerTime: {ServerTime}\nOffsetMs: {OffsetMs}",
                localUtcMs, serverTimeMs, newOffset);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bybit time synchronization failed. Using previous offset.");
        }
    }

    /// <summary>
    /// Starts initial synchronization and runs periodic refresh loop in the background.
    /// </summary>
    public async Task StartBackgroundSyncAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Bybit time synchronization background service...");

        // Perform initial sync on application startup
        try
        {
            await SyncTimeAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Initial Bybit time synchronization failed during startup. Defaulting to system clock offset.");
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _timerTask = RefreshLoopAsync(_cts.Token);
        await _timerTask;
    }

    #region IHostedService Implementation

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();
        _timerTask = StartBackgroundSyncAsync(_cts.Token);
        return Task.CompletedTask;
    }

    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        var syncOptions = _settings.TimeSync ?? new BybitTimeSyncOptions();
        var intervalMinutes = syncOptions.RefreshIntervalMinutes > 0 ? syncOptions.RefreshIntervalMinutes : 5;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), cancellationToken);
                await SyncTimeAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in Bybit time synchronization refresh loop.");
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Bybit time synchronization background service...");
        if (_cts != null)
        {
            _cts.Cancel();
            if (_timerTask != null)
            {
                await Task.WhenAny(_timerTask, Task.Delay(Timeout.Infinite, cancellationToken));
            }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }

    #endregion
}
