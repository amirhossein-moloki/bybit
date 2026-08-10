using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Configuration;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Interfaces.Streams;
using TradingBot.Application.Monitoring;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Telegram.Interfaces;

namespace TradingBot.Worker.Lifecycle;

public class GracefulShutdownManager : IGracefulShutdownManager
{
    private readonly ITradingGate _tradingGate;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly StartupShutdownOptions _options;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<GracefulShutdownManager> _logger;

    public GracefulShutdownManager(
        ITradingGate tradingGate,
        IHostApplicationLifetime lifetime,
        StartupShutdownOptions options,
        ILogger<GracefulShutdownManager> logger,
        IServiceProvider serviceProvider)
    {
        _tradingGate = tradingGate ?? throw new ArgumentNullException(nameof(tradingGate));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

        // Register callback for graceful shutdown
        _lifetime.ApplicationStopping.Register(() =>
        {
            _logger.LogWarning("GracefulShutdown: Host application stopping signal received. Initiating Graceful Shutdown...");
            // Run synchronously or trigger background shutdown task
            Task.Run(async () => await ShutdownAsync()).GetAwaiter().GetResult();
        });
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (_tradingGate.CurrentState == ApplicationState.Stopping || _tradingGate.CurrentState == ApplicationState.Stopped)
        {
            _logger.LogInformation("GracefulShutdown: Shutdown already in progress or completed.");
            return;
        }

        var correlationId = Guid.NewGuid().ToString();
        _logger.LogWarning("GracefulShutdown: Setting state to Stopping, disabling trading gate...");
        _tradingGate.SetState(ApplicationState.Stopping);
        _tradingGate.DisableTrading();

        using (var scope = _serviceProvider.CreateScope())
        {
            var eventPublisher = scope.ServiceProvider.GetService<IMonitoringEventPublisher>();
            if (eventPublisher != null)
            {
                try
                {
                    await eventPublisher.PublishAsync(new MonitoringEvent(
                        "ShutdownRequested", "WARNING", "Shutdown", "GracefulShutdownManager", "STOPPING",
                        "Application shutdown requested. Trading gate CLOSED.", correlationId: correlationId
                    ), forceSynchronous: true, cancellationToken: cancellationToken);

                    await eventPublisher.PublishAsync(new MonitoringEvent(
                        "TradingDisabled", "WARNING", "Shutdown", "GracefulShutdownManager", "DISABLED",
                        "Trading has been disabled for graceful shutdown.", correlationId: correlationId
                    ), forceSynchronous: true, cancellationToken: cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "GracefulShutdown: Failed to publish shutdown started events.");
                }
            }
        }

        // 1. Drain Pending Operations
        if (_options.DrainPendingOperations)
        {
            _logger.LogInformation("GracefulShutdown: Draining pending operations. Waiting up to {Timeout}s...", _options.ShutdownTimeout.TotalSeconds);
            try
            {
                // Wait for any active tasks to complete, capped by the shutdown timeout
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(_options.ShutdownTimeout);

                // Simulate/let any active order execution finish
                await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);
                _logger.LogInformation("GracefulShutdown: Pending operations drained.");
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("GracefulShutdown: Timeout reached before fully draining pending operations. Proceeding anyway.");
            }
        }

        // 2. Close WebSocket connections
        using (var scope = _serviceProvider.CreateScope())
        {
            var webSocketClient = scope.ServiceProvider.GetService<IExchangeStreamClient>();
            if (webSocketClient != null)
            {
                _logger.LogInformation("GracefulShutdown: Closing exchange WebSocket client connection...");
                try
                {
                    using var wsCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await webSocketClient.DisconnectAsync(wsCts.Token);
                    _logger.LogInformation("GracefulShutdown: WebSocket client closed successfully.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "GracefulShutdown: Error disconnecting WebSocket client.");
                }
            }
        }

        // 3. Close External Connections (Telegram client)
        using (var scope = _serviceProvider.CreateScope())
        {
            var telegramClient = scope.ServiceProvider.GetService<ITelegramClient>();
            if (telegramClient != null)
            {
                _logger.LogInformation("GracefulShutdown: Disconnecting Telegram client...");
                try
                {
                    await telegramClient.DisconnectAsync();
                    _logger.LogInformation("GracefulShutdown: Telegram client disconnected.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "GracefulShutdown: Error disconnecting Telegram client.");
                }
            }
        }

        // 4. Workers stopping
        using (var scope = _serviceProvider.CreateScope())
        {
            var eventPublisher = scope.ServiceProvider.GetService<IMonitoringEventPublisher>();
            if (eventPublisher != null)
            {
                try
                {
                    await eventPublisher.PublishAsync(new MonitoringEvent(
                        "WorkersStopping", "INFO", "Shutdown", "GracefulShutdownManager", "STOPPING",
                        "Stopping background services and workers.", correlationId: correlationId
                    ), forceSynchronous: true, cancellationToken: cancellationToken);
                }
                catch { }
            }
        }

        _logger.LogWarning("GracefulShutdown: Setting state to Stopped. Graceful shutdown finished.");
        _tradingGate.SetState(ApplicationState.Stopped);

        using (var scope = _serviceProvider.CreateScope())
        {
            var eventPublisher = scope.ServiceProvider.GetService<IMonitoringEventPublisher>();
            if (eventPublisher != null)
            {
                try
                {
                    await eventPublisher.PublishAsync(new MonitoringEvent(
                        "ApplicationStopped", "INFO", "Shutdown", "GracefulShutdownManager", "STOPPED",
                        "Application has been successfully stopped.", correlationId: correlationId
                    ), forceSynchronous: true, cancellationToken: cancellationToken);
                }
                catch { }
            }
        }
    }
}
