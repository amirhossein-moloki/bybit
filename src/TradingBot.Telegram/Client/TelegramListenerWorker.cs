using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBot.Telegram.Configuration;
using TradingBot.Telegram.Interfaces;
using TradingBot.Telegram.Models;
using TradingBot.Telegram.Exceptions;

namespace TradingBot.Telegram.Client;

public class TelegramListenerWorker : BackgroundService
{
    private readonly ITelegramClient _client;
    private readonly ITelegramAuthenticationService _authService;
    private readonly TelegramOptions _options;
    private readonly ILogger<TelegramListenerWorker> _logger;

    public TelegramListenerWorker(
        ITelegramClient client,
        ITelegramAuthenticationService authService,
        IOptions<TelegramOptions> options,
        ILogger<TelegramListenerWorker> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Telegram receiver background worker is disabled in configuration.");
            return;
        }

        _logger.LogInformation("Telegram listener background worker starting...");

        int retryCount = 0;
        const int maxRetries = 10;
        var backoffDelay = TimeSpan.FromSeconds(2);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_client.IsConnected() || _client.CurrentState != TelegramConnectionState.Listening)
                {
                    if (retryCount > 0)
                    {
                        if (_client is TelegramClientService cs)
                        {
                            cs.SetState(TelegramConnectionState.Reconnecting);
                        }
                        _logger.LogInformation("Reconnect Attempt {Count}/{Max} in {Delay} seconds...", retryCount, maxRetries, backoffDelay.TotalSeconds);
                        await Task.Delay(backoffDelay, stoppingToken);

                        // Exponential backoff with a maximum delay of 60 seconds
                        backoffDelay = TimeSpan.FromSeconds(Math.Min(backoffDelay.TotalSeconds * 2, 60));
                    }

                    _logger.LogInformation("Connecting to Telegram...");
                    await _client.ConnectAsync();
                    _logger.LogInformation("Telegram Connected");

                    _logger.LogInformation("Authenticating with Telegram...");
                    await _authService.AuthenticateAsync();
                    _logger.LogInformation("Authentication Completed");

                    _logger.LogInformation("Initializing Update Listener...");
                    await _client.InitializeListeningAsync();

                    if (_client is TelegramClientService service)
                    {
                        service.SetState(TelegramConnectionState.Listening);
                    }
                    _logger.LogInformation("Listening Started");

                    // Connection succeeded, reset backoff parameters
                    retryCount = 0;
                    backoffDelay = TimeSpan.FromSeconds(2);
                }

                // Wait / keep-alive loop step
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Telegram listener background worker is stopping due to cancellation.");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred in Telegram listener background loop.");

                if (_client is TelegramClientService service)
                {
                    service.SetState(TelegramConnectionState.Error);
                }

                retryCount++;
                if (retryCount >= maxRetries)
                {
                    _logger.LogCritical("Telegram listener worker reached maximum reconnection retries ({Max}). Stopping.", maxRetries);
                    break;
                }
            }
        }

        _logger.LogInformation("Disconnected");
        try
        {
            await _client.DisconnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting Telegram client during shutdown.");
        }
        _logger.LogInformation("Telegram listener background worker stopped.");
    }
}
