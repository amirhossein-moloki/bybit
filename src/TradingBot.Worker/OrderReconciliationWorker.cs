using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Trading.Execution.Contracts;

namespace TradingBot.Worker;

public class OrderReconciliationWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OrderReconciliationWorker> _logger;
    private readonly TradingBot.Application.Monitoring.IWorkerHealthRegistry _healthRegistry;

    public OrderReconciliationWorker(
        IServiceProvider serviceProvider,
        ILogger<OrderReconciliationWorker> logger,
        TradingBot.Application.Monitoring.IWorkerHealthRegistry healthRegistry)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _healthRegistry = healthRegistry ?? throw new ArgumentNullException(nameof(healthRegistry));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _healthRegistry.RegisterWorker(nameof(OrderReconciliationWorker), isCritical: false);
        _logger.LogInformation("OrderReconciliationWorker: Starting background worker...");

        while (!stoppingToken.IsCancellationRequested)
        {
            _healthRegistry.RecordHeartbeat(nameof(OrderReconciliationWorker), "Running");
            try
            {
                // Sensible polling interval: e.g. 5 seconds
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

                _logger.LogInformation("OrderReconciliationWorker: Initiating background reconciliation pass...");

                using var scope = _serviceProvider.CreateScope();
                var reconciliationService = scope.ServiceProvider.GetRequiredService<IOrderReconciliationService>();

                await reconciliationService.ReconcileAsync(stoppingToken);

                _logger.LogInformation("OrderReconciliationWorker: Background reconciliation pass completed successfully.");
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("OrderReconciliationWorker: Received cancellation, shutting down...");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OrderReconciliationWorker: Error occurred during background reconciliation pass.");
            }
        }
    }
}
