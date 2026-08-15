using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using TradingBot.Telegram.Configuration;
using TradingBot.Telegram.Interfaces;
using TradingBot.Telegram.Models;
using TradingBot.Telegram.Exceptions;

namespace TradingBot.Telegram.Client;

public class TelegramListenerWorker : BackgroundService
{
    private readonly ITelegramClient _client;
    private readonly ITelegramAuthenticationService _authService;
    private readonly ITelegramSessionManager _sessionManager;
    private readonly TelegramOptions _options;
    private readonly ILogger<TelegramListenerWorker> _logger;
    private readonly ResiliencePipeline _resiliencePipeline;

    public TelegramListenerWorker(
        ITelegramClient client,
        ITelegramAuthenticationService authService,
        ITelegramSessionManager sessionManager,
        IOptions<TelegramOptions> options,
        ILogger<TelegramListenerWorker> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _resiliencePipeline = new ResiliencePipelineBuilder()
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(30),
                OnTimeout = args =>
                {
                    _logger.LogWarning("Telegram Connection: Operation timed out.");
                    return default;
                }
            })
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                MaxRetryAttempts = 10,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(2),
                MaxDelay = TimeSpan.FromSeconds(60),
                OnRetry = args =>
                {
                    _client.SetState(TelegramConnectionState.Reconnecting);
                    _logger.LogWarning("Reconnect Attempt {Count}/10 in {Delay} seconds due to: {Exception}",
                        args.AttemptNumber + 1, args.RetryDelay.TotalSeconds, args.Outcome.Exception?.Message);
                    return default;
                }
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<Exception>(),
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromMinutes(2),
                MinimumThroughput = 3,
                BreakDuration = TimeSpan.FromSeconds(30),
                OnOpened = args =>
                {
                    _logger.LogError("Resilience: Telegram Connection Circuit Breaker OPENED for {BreakDuration} due to: {Exception}",
                        args.BreakDuration, args.Outcome.Exception?.Message);
                    return default;
                },
                OnClosed = args =>
                {
                    _logger.LogInformation("Resilience: Telegram Connection Circuit Breaker CLOSED.");
                    return default;
                }
            })
            .Build();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Telegram receiver background worker is disabled in configuration.");
            return;
        }

        _logger.LogInformation("Telegram listener background worker starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_sessionManager.SessionExists() || _client.CurrentState == TelegramConnectionState.Authenticating)
                {
                    if (_client.CurrentState != TelegramConnectionState.NotConnected && _client.CurrentState != TelegramConnectionState.Authenticating)
                    {
                        _client.SetState(TelegramConnectionState.NotConnected);
                        _logger.LogInformation("No Telegram session found. Waiting for Dashboard authentication...");
                    }

                    await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                    continue;
                }

                if (!_client.IsConnected() || _client.CurrentState != TelegramConnectionState.Listening)
                {
                    await _resiliencePipeline.ExecuteAsync(async ct =>
                    {
                        _logger.LogInformation("Connecting to Telegram...");
                        await _client.ConnectAsync();
                        _logger.LogInformation("Telegram Connected");

                        _logger.LogInformation("Authenticating with Telegram...");
                        await _authService.AuthenticateAsync();
                        _logger.LogInformation("Authentication Completed");

                        _logger.LogInformation("Initializing Update Listener...");
                        await _client.InitializeListeningAsync();

                        _client.SetState(TelegramConnectionState.Listening);
                        _logger.LogInformation("Listening Started");
                    }, stoppingToken);
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

                if (!_sessionManager.SessionExists())
                {
                    _client.SetState(TelegramConnectionState.NotConnected);
                    _logger.LogInformation("Telegram session not present. Waiting for Dashboard authentication...");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                _client.SetState(TelegramConnectionState.Error);
                _logger.LogWarning("Telegram listener worker encountered an error. Retrying in 10 seconds...");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
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
