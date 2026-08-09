using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Monitoring;
using TradingBot.Application.Monitoring.Configuration;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;

namespace TradingBot.Worker;

public class MonitoringWorker : BackgroundService
{
    private readonly IHealthStatusProvider _healthStatusProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly MonitoringOptions _options;
    private readonly ILogger<MonitoringWorker> _logger;
    private readonly Dictionary<string, (string Status, bool IsStale)> _lastWorkerStates = new(StringComparer.OrdinalIgnoreCase);

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

        // Emit worker started system event for ourselves
        using (var scope = _serviceProvider.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetService<IMonitoringEventPublisher>();
            if (publisher != null)
            {
                await publisher.PublishAsync(new MonitoringEvent(
                    "WorkerStarted",
                    "INFORMATION",
                    "Worker",
                    nameof(MonitoringWorker),
                    "Started",
                    $"Worker '{nameof(MonitoringWorker)}' has started."
                ), forceSynchronous: false, cancellationToken: stoppingToken);
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug("MonitoringWorker: Initiating periodic health evaluation pass...");

                // Create scope to resolve scoped IHealthCheckEngine, IHealthCheckResultRepository, IMonitoringEventPublisher and IUnitOfWork
                using (var scope = _serviceProvider.CreateScope())
                {
                    var healthCheckEngine = scope.ServiceProvider.GetRequiredService<IHealthCheckEngine>();
                    var repository = scope.ServiceProvider.GetRequiredService<IHealthCheckResultRepository>();
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                    var publisher = scope.ServiceProvider.GetService<IMonitoringEventPublisher>();

                    // Execute all eligible checks with error isolation and timeout protection
                    var results = await healthCheckEngine.RunAllChecksAsync(stoppingToken);

                    var hasUpdates = false;
                    foreach (var result in results)
                    {
                        hasUpdates = true;

                        // Retrieve the previous health check status (Section 37 & 64)
                        var previousResult = _healthStatusProvider.GetComponentStatus(result.ServiceName);
                        var statusChanged = previousResult != null && previousResult.Status != result.Status;

                        // 1. Update fast in-memory current state cache
                        _healthStatusProvider.UpdateStatus(result.ServiceName, result);

                        // 2. Persist historical result into DB
                        await repository.AddAsync(result, stoppingToken);

                        _logger.LogInformation("MonitoringWorker: Component '{Component}' health status evaluated as '{Status}' in {Duration}ms.",
                            result.ServiceName, result.Status, result.DurationMs);

                        // If status transitioned, publish a transition event (Section 37 & 64)
                        if (statusChanged && publisher != null)
                        {
                            var severity = result.Status == HealthStatus.Healthy ? "INFORMATION" : (result.Status == HealthStatus.Degraded ? "WARNING" : "ERROR");
                            var monitoringEvent = new MonitoringEvent(
                                "HealthStatusChanged",
                                severity,
                                "Monitoring",
                                "HealthCheckEngine",
                                "Detected",
                                $"Component '{result.ServiceName}' health status transitioned from {(previousResult?.Status.ToString() ?? "Unknown")} to {result.Status}.",
                                errorCode: result.ErrorCode,
                                exceptionType: result.ErrorMessage != null ? "HealthCheckException" : null,
                                payload: result.Metadata
                            );
                            await publisher.PublishAsync(monitoringEvent, forceSynchronous: false, cancellationToken: stoppingToken);
                        }
                    }

                    if (hasUpdates)
                    {
                        // Save all records atomically
                        try
                        {
                            await unitOfWork.SaveChangesAsync(stoppingToken);
                        }
                        catch (Exception dbEx)
                        {
                            _logger.LogError(dbEx, "MonitoringWorker: Failed to persist health check results to database. In-memory health state is still preserved.");
                        }
                        _logger.LogInformation("MonitoringWorker: Health pass complete. Overall system status: '{OverallStatus}'.",
                            _healthStatusProvider.GetOverallStatus());
                    }

                    // Process and evaluate Worker Lifecycles and Heartbeats (Section 36 & 64)
                    var workerRegistry = scope.ServiceProvider.GetService<IWorkerHealthRegistry>();
                    if (workerRegistry != null && publisher != null)
                    {
                        var heartbeats = workerRegistry.GetWorkerHeartbeats();
                        var now = DateTime.UtcNow;
                        var staleThreshold = TimeSpan.FromSeconds(_options.Workers.StaleThresholdSeconds);

                        foreach (var hb in heartbeats.Values)
                        {
                            var isStale = (now - hb.LastHeartbeatAt) > staleThreshold;
                            var currentStatus = hb.Status;

                            _lastWorkerStates.TryGetValue(hb.WorkerName, out var lastState);

                            bool statusChanged = lastState.Status != currentStatus;
                            bool staleChanged = lastState.IsStale != isStale;

                            if (statusChanged || staleChanged || !string.Equals(lastState.Status, currentStatus, StringComparison.OrdinalIgnoreCase))
                            {
                                if (statusChanged && string.Equals(currentStatus, "Failed", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Emit WorkerFailed
                                    var monitoringEvent = new MonitoringEvent(
                                        "WorkerFailed",
                                        "ERROR",
                                        "Worker",
                                        hb.WorkerName,
                                        "Failed",
                                        $"Worker '{hb.WorkerName}' failed: {hb.LastErrorMessage ?? "Unknown error"}",
                                        errorCode: "WORKER_FAILED",
                                        exceptionType: hb.LastErrorMessage != null ? "WorkerExecutionException" : null
                                    );
                                    await publisher.PublishAsync(monitoringEvent, forceSynchronous: false, cancellationToken: stoppingToken);
                                }

                                if (staleChanged && isStale)
                                {
                                    // Emit WorkerHeartbeatLost
                                    var monitoringEvent = new MonitoringEvent(
                                        "WorkerHeartbeatLost",
                                        "WARNING",
                                        "Worker",
                                        hb.WorkerName,
                                        "Detected",
                                        $"Worker '{hb.WorkerName}' heartbeat lost. Last heartbeat was {(now - hb.LastHeartbeatAt).TotalSeconds:F1}s ago.",
                                        errorCode: "WORKER_HEARTBEAT_LOST"
                                    );
                                    await publisher.PublishAsync(monitoringEvent, forceSynchronous: false, cancellationToken: stoppingToken);
                                }

                                if (statusChanged)
                                {
                                    if (string.Equals(currentStatus, "Started", StringComparison.OrdinalIgnoreCase))
                                    {
                                        var monitoringEvent = new MonitoringEvent(
                                            "WorkerStarted",
                                            "INFORMATION",
                                            "Worker",
                                            hb.WorkerName,
                                            "Started",
                                            $"Worker '{hb.WorkerName}' has started."
                                        );
                                        await publisher.PublishAsync(monitoringEvent, forceSynchronous: false, cancellationToken: stoppingToken);
                                    }
                                    else if (string.Equals(currentStatus, "Stopped", StringComparison.OrdinalIgnoreCase))
                                    {
                                        var monitoringEvent = new MonitoringEvent(
                                            "WorkerStopped",
                                            "INFORMATION",
                                            "Worker",
                                            hb.WorkerName,
                                            "Stopped",
                                            $"Worker '{hb.WorkerName}' has stopped."
                                        );
                                        await publisher.PublishAsync(monitoringEvent, forceSynchronous: false, cancellationToken: stoppingToken);
                                    }
                                }

                                _lastWorkerStates[hb.WorkerName] = (currentStatus, isStale);
                            }
                        }
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

        // Emit worker stopped system event
        using (var scope = _serviceProvider.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetService<IMonitoringEventPublisher>();
            if (publisher != null)
            {
                await publisher.PublishAsync(new MonitoringEvent(
                    "WorkerStopped",
                    "INFORMATION",
                    "Worker",
                    nameof(MonitoringWorker),
                    "Stopped",
                    $"Worker '{nameof(MonitoringWorker)}' has stopped gracefully."
                ), forceSynchronous: false, cancellationToken: default);
            }
        }

        _logger.LogInformation("MonitoringWorker: Stopped.");
    }
}
