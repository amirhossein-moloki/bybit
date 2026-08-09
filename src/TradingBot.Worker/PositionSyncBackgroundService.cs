using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Interfaces.Streams;

namespace TradingBot.Worker;

public class PositionSyncBackgroundService : BackgroundService
{
    private readonly IPositionStream _positionStream;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PositionSyncBackgroundService> _logger;
    private readonly TradingBot.Application.Monitoring.IWorkerHealthRegistry _healthRegistry;

    public PositionSyncBackgroundService(
        IPositionStream positionStream,
        IServiceProvider serviceProvider,
        ILogger<PositionSyncBackgroundService> logger,
        TradingBot.Application.Monitoring.IWorkerHealthRegistry healthRegistry)
    {
        _positionStream = positionStream ?? throw new ArgumentNullException(nameof(positionStream));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _healthRegistry = healthRegistry ?? throw new ArgumentNullException(nameof(healthRegistry));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        _healthRegistry.RegisterWorker(nameof(PositionSyncBackgroundService), isCritical: true);
        _logger.LogInformation("PositionSyncBackgroundService: Starting...");

        // 1. Subscribe to the position stream
        try
        {
            await _positionStream.SubscribeAsync(stoppingToken);
            _logger.LogInformation("PositionSyncBackgroundService: Subscribed to position stream.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PositionSyncBackgroundService: Failed to subscribe to position stream.");
        }

        // 2. Start a background loop to perform periodic REST reconciliation (Item 15 REST Fallback)
        _ = RunPeriodicReconciliationAsync(stoppingToken);

        // 3. Process incoming position stream events
        try
        {
            await foreach (var positionUpdate in _positionStream.ReceiveEventsAsync(stoppingToken))
            {
                _healthRegistry.RecordHeartbeat(nameof(PositionSyncBackgroundService), "Running");
                _logger.LogInformation("PositionSyncBackgroundService: Received position update - Symbol: {Symbol}, Size: {Size}, Side: {Side}",
                    positionUpdate.Symbol, positionUpdate.Size, positionUpdate.Side);

                using var scope = _serviceProvider.CreateScope();
                var syncService = scope.ServiceProvider.GetRequiredService<IPositionSynchronizationService>();

                try
                {
                    await syncService.SynchronizeAsync(stoppingToken);
                    _logger.LogInformation("PositionSyncBackgroundService: Real-time position synchronization completed successfully.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "PositionSyncBackgroundService: Error during real-time position synchronization.");
                }
            }
        }
        catch (OperationCanceledException)
        {
            _healthRegistry.RecordHeartbeat(nameof(PositionSyncBackgroundService), "Stopping");
            _logger.LogInformation("PositionSyncBackgroundService: Cancelled.");
        }
        catch (Exception ex)
        {
            _healthRegistry.RecordHeartbeat(nameof(PositionSyncBackgroundService), "Failed", ex.Message);
            _logger.LogError(ex, "PositionSyncBackgroundService: Exception in position sync receive loop.");
        }

        _healthRegistry.RecordHeartbeat(nameof(PositionSyncBackgroundService), "Stopped");
    }

    private async Task RunPeriodicReconciliationAsync(CancellationToken cancellationToken)
    {
        // Controlled polling interval to avoid exchange rate limits (Item 15)
        var interval = TimeSpan.FromMinutes(5);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                _healthRegistry.RecordHeartbeat(nameof(PositionSyncBackgroundService), "Running");
                await Task.Delay(interval, cancellationToken);

                _logger.LogInformation("PositionSyncBackgroundService: Running scheduled periodic REST reconciliation fallback...");

                using var scope = _serviceProvider.CreateScope();
                var reconciliationService = scope.ServiceProvider.GetRequiredService<IPositionReconciliationService>();
                await reconciliationService.ReconcileAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PositionSyncBackgroundService: Error in periodic scheduled REST reconciliation.");
            }
        }
    }
}
