using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Monitoring;

namespace TradingBot.Worker;

public class AlertEvaluationWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AlertEvaluationWorker> _logger;
    private readonly IWorkerHealthRegistry _healthRegistry;

    public AlertEvaluationWorker(
        IServiceProvider serviceProvider,
        ILogger<AlertEvaluationWorker> logger,
        IWorkerHealthRegistry healthRegistry)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _healthRegistry = healthRegistry ?? throw new ArgumentNullException(nameof(healthRegistry));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        _healthRegistry.RegisterWorker(nameof(AlertEvaluationWorker), isCritical: false);
        _logger.LogInformation("AlertEvaluationWorker: Starting active time-based alert evaluation worker...");

        var metricsService = _serviceProvider.GetService<IMetricsService>();
        metricsService?.RecordWorkerStart(nameof(AlertEvaluationWorker));

        while (!stoppingToken.IsCancellationRequested)
        {
            _healthRegistry.RecordHeartbeat(nameof(AlertEvaluationWorker), "Running");
            metricsService?.RecordWorkerHeartbeat(nameof(AlertEvaluationWorker), "Running");

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var alertEngine = scope.ServiceProvider.GetRequiredService<IAlertEngine>();

                var start = DateTime.UtcNow;
                await alertEngine.EvaluateActiveAlertsAsync(stoppingToken);
                var duration = (DateTime.UtcNow - start).TotalMilliseconds;

                metricsService?.RecordLatency("Monitoring Event → Alert Evaluation", duration);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AlertEvaluationWorker: Error occurred while evaluating active alerts.");
                _healthRegistry.RecordHeartbeat(nameof(AlertEvaluationWorker), "Failed", ex.Message);
                metricsService?.RecordWorkerFailure(nameof(AlertEvaluationWorker), ex.Message);
            }

            try
            {
                // Evaluate every 5 seconds for responsive threshold detection
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("AlertEvaluationWorker: Stopped.");
        metricsService?.RecordWorkerHeartbeat(nameof(AlertEvaluationWorker), "Stopped");
    }
}
