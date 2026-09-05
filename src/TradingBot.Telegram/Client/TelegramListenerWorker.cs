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
using TL;
using TradingBot.Telegram.Authentication;
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
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ex =>
                {
                    if (ex is RpcException rpcEx && (rpcEx.Code == 420 || rpcEx.Message.Contains("FLOOD_WAIT")))
                        return false;
                    if (ex.InnerException is RpcException innerRpc && (innerRpc.Code == 420 || innerRpc.Message.Contains("FLOOD_WAIT")))
                        return false;
                    if (ex is TelegramAuthenticationException)
                        return false;
                    return true;
                }),
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
                ShouldHandle = new PredicateBuilder().Handle<Exception>(ex =>
                {
                    if (ex is RpcException rpcEx && (rpcEx.Code == 420 || rpcEx.Message.Contains("FLOOD_WAIT")))
                        return false;
                    if (ex.InnerException is RpcException innerRpc && (innerRpc.Code == 420 || innerRpc.Message.Contains("FLOOD_WAIT")))
                        return false;
                    if (ex is TelegramAuthenticationException)
                        return false;
                    return true;
                }),
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
                if (_client.IsInFloodWait(out var remaining))
                {
                    _logger.LogWarning("Telegram client is in FLOOD_WAIT cooldown. Halting background reconnect attempts for {RemainingSeconds}s...", Math.Ceiling(remaining.TotalSeconds));
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(remaining.TotalSeconds, 15)), stoppingToken);
                    continue;
                }

                if (!_sessionManager.SessionExists() ||
                    _client.CurrentState == TelegramConnectionState.RequiresAuthentication ||
                    _client.CurrentState == TelegramConnectionState.AuthenticationFailed ||
                    _client.CurrentState == TelegramConnectionState.NotConnected ||
                    _client.CurrentState == TelegramConnectionState.Authenticating)
                {
                    if (_client.CurrentState != TelegramConnectionState.NotConnected &&
                        _client.CurrentState != TelegramConnectionState.RequiresAuthentication &&
                        _client.CurrentState != TelegramConnectionState.AuthenticationFailed &&
                        _client.CurrentState != TelegramConnectionState.Authenticating)
                    {
                        _client.SetState(TelegramConnectionState.RequiresAuthentication);
                        _logger.LogWarning("No valid Telegram session found or authentication is required. Pausing background listener loop. Waiting for Dashboard authentication...");
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

                        _logger.LogInformation("Performing passive Telegram session verification...");
                        await _authService.AuthenticateAsync();

                        if (_client.CurrentState == TelegramConnectionState.RequiresAuthentication)
                        {
                            _logger.LogWarning("Passive session check indicated authentication is required. Aborting listener initialization until user logs in via Dashboard.");
                            return;
                        }

                        _logger.LogInformation("Initializing Update Listener...");
                        await _client.InitializeListeningAsync();

                        _client.SetState(TelegramConnectionState.Listening);
                        _logger.LogInformation("Telegram Listener Started Successfully.");
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
            catch (RpcException rpcEx) when (rpcEx.Code == 420 || rpcEx.Message.Contains("FLOOD_WAIT"))
            {
                int seconds = TelegramAuthService.ExtractFloodWaitSeconds(rpcEx);
                _client.SetFloodWait(seconds);
                _logger.LogError(rpcEx, "Telegram FLOOD_WAIT_420 encountered in listener worker loop. Cooldown initiated for {Seconds} seconds.", seconds);
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(seconds, 30)), stoppingToken);
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

                if (_client.CurrentState != TelegramConnectionState.RequiresAuthentication &&
                    _client.CurrentState != TelegramConnectionState.AuthenticationFailed)
                {
                    _client.SetState(TelegramConnectionState.Error);
                }

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
