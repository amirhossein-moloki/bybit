using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Monitoring;
using TradingBot.Application.Monitoring.Configuration;
using TradingBot.Application.Repositories;

namespace TradingBot.Worker;

public class MonitoringWorker : BackgroundService
{
    private readonly IHealthStatusProvider _healthStatusProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly MonitoringOptions _options;
    private readonly ILogger<MonitoringWorker> _logger;

    public MonitoringWorker(
        IHealthStatusProvider healthStatusProvider,
        IServiceProvider serviceProvider,
        MonitoringOptions options,
        ILogger<MonitoringWorker> logger)
    {
        _healthStatusProvider = healthStatusProvider ?? throw new ArgumentNullException(nameof(healthStatusProvider));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("MonitoringWorker: Disabled in configuration. Skipping execution.");
            return;
        }

        _logger.LogInformation("MonitoringWorker: Starting health monitoring background loop...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug("MonitoringWorker: Initiating periodic health evaluation pass...");

                // Create scope to resolve scoped IHealthCheckEngine, IHealthCheckResultRepository and IUnitOfWork
                using (var scope = _serviceProvider.CreateScope())
                {
                    var healthCheckEngine = scope.ServiceProvider.GetRequiredService<IHealthCheckEngine>();
                    var repository = scope.ServiceProvider.GetRequiredService<IHealthCheckResultRepository>();
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                    // Execute all eligible checks with error isolation and timeout protection
                    var results = await healthCheckEngine.RunAllChecksAsync(stoppingToken);

                    var hasUpdates = false;
                    foreach (var result in results)
                    {
                        hasUpdates = true;
                        // 1. Update fast in-memory current state cache
                        _healthStatusProvider.UpdateStatus(result.ServiceName, result);

                        // 2. Persist historical result into DB
                        await repository.AddAsync(result, stoppingToken);

                        _logger.LogInformation("MonitoringWorker: Component '{Component}' health status evaluated as '{Status}' in {Duration}ms.",
                            result.ServiceName, result.Status, result.DurationMs);
                    }

                    if (hasUpdates)
                    {
                        // Save all records atomically
                        await unitOfWork.SaveChangesAsync(stoppingToken);
                        _logger.LogInformation("MonitoringWorker: Health pass complete. Overall system status: '{OverallStatus}'.",
                            _healthStatusProvider.GetOverallStatus());
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("MonitoringWorker: Stopping gracefully due to cancellation request...");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MonitoringWorker: Unhandled exception during monitoring loop.");
            }

            try
            {
                // Run on a fast 1-second tick loop to evaluate check intervals and respond immediately to cancellation
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("MonitoringWorker: Stopped.");
    }
}
