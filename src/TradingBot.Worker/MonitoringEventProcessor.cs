using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Monitoring;
using TradingBot.Application.Monitoring.Configuration;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;

namespace TradingBot.Worker;

public class MonitoringEventProcessor : BackgroundService
{
    private readonly IMonitoringEventQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly MonitoringOptions _options;
    private readonly ILogger<MonitoringEventProcessor> _logger;
    private readonly IWorkerHealthRegistry _healthRegistry;

    public MonitoringEventProcessor(
        IMonitoringEventQueue queue,
        IServiceProvider serviceProvider,
        MonitoringOptions options,
        ILogger<MonitoringEventProcessor> logger,
        IWorkerHealthRegistry healthRegistry,
        IHostApplicationLifetime appLifetime)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _healthRegistry = healthRegistry ?? throw new ArgumentNullException(nameof(healthRegistry));

        if (appLifetime != null)
        {
            appLifetime.ApplicationStarted.Register(() => OnApplicationStarted());
            appLifetime.ApplicationStopping.Register(() => OnApplicationStopping());
            appLifetime.ApplicationStopped.Register(() => OnApplicationStopped());
        }
    }

    private void OnApplicationStarted()
    {
        _ = PublishLifetimeEventAsync("ApplicationStarted", "INFORMATION", "Application started successfully.", "Started");
    }

    private void OnApplicationStopping()
    {
        _ = PublishLifetimeEventAsync("ApplicationStopping", "INFORMATION", "Application is stopping...", "Stopping");
    }

    private void OnApplicationStopped()
    {
        _ = PublishLifetimeEventAsync("ApplicationStopped", "INFORMATION", "Application stopped.", "Stopped");
    }

    private async Task PublishLifetimeEventAsync(string eventType, string severity, string message, string status)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var publisher = scope.ServiceProvider.GetRequiredService<IMonitoringEventPublisher>();
            await publisher.PublishAsync(new MonitoringEvent(
                eventType,
                severity,
                "System",
                "Host",
                status,
                message
            ), forceSynchronous: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish application lifetime event: {EventType}", eventType);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // Let worker yield startup thread

        _healthRegistry.RegisterWorker(nameof(MonitoringEventProcessor), isCritical: false);
        _logger.LogInformation("MonitoringEventProcessor: Starting event processing queue consumer...");

        while (!stoppingToken.IsCancellationRequested)
        {
            _healthRegistry.RecordHeartbeat(nameof(MonitoringEventProcessor), "Running");
            MonitoringEvent? @event = null;

            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));

                // Dequeue next monitoring event with 3s heartbeat timeout
                @event = await _queue.DequeueAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                // Timeout waiting for event; loop around to update heartbeat
                continue;
            }

            try
            {
                if (@event == null)
                {
                    continue;
                }

                if (!_options.Observability.PersistenceEnabled)
                {
                    continue; // Skip database persistence if disabled in config
                }

                // Create scope to resolve scoped DB context & repository
                using (var scope = _serviceProvider.CreateScope())
                {
                    var repository = scope.ServiceProvider.GetRequiredService<IMonitoringEventRepository>();
                    var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                    // Check for duplicate if ExternalEventId exists
                    if (!string.IsNullOrWhiteSpace(@event.ExternalEventId))
                    {
                        var spec = new TradingBot.Persistence.Repositories.MonitoringEventByExternalIdSpecification(@event.Source, @event.ExternalEventId);
                        var existing = await repository.GetAsync(spec, stoppingToken);
                        if (existing.Any())
                        {
                            _logger.LogDebug("MonitoringEventProcessor: Duplicate event ignored. Source: {Source}, ExternalId: {ExternalId}",
                                @event.Source, @event.ExternalEventId);
                            continue; // Skip persisting duplicate
                        }
                    }

                    try
                    {
                        await repository.AddAsync(@event, stoppingToken);
                        await unitOfWork.SaveChangesAsync(stoppingToken);
                    }
                    catch (Exception dbEx)
                    {
                        _logger.LogError(dbEx, "MonitoringEventProcessor: Failed to persist monitoring event to database. Proceeding with alerting and notifications.");
                    }

                    // Record system event metrics
                    var metricsService = scope.ServiceProvider.GetService<IMetricsService>();
                    if (@event.Severity == "CRITICAL")
                    {
                        metricsService?.IncrementSystemCriticalErrors();
                    }
                    else if (@event.Severity == "ERROR")
                    {
                        metricsService?.IncrementSystemErrors();
                    }
                    else if (@event.Severity == "WARNING")
                    {
                        metricsService?.IncrementSystemWarnings();
                    }

                    // Call the alert engine to evaluate rules
                    var alertEngine = scope.ServiceProvider.GetRequiredService<IAlertEngine>();
                    bool handledAsAlert = await alertEngine.ProcessEventAsync(@event, stoppingToken);

                    if (!handledAsAlert)
                    {
                        // Fall back to direct notifications for non-alert events
                        var notificationEngine = scope.ServiceProvider.GetRequiredService<INotificationEngine>();
                        await notificationEngine.ProcessEventAsync(@event, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Isolate event processing error so it does not stop the worker or falsely mark the background process as Failed
                _healthRegistry.RecordHeartbeat(nameof(MonitoringEventProcessor), "Running", ex.Message);

                _logger.LogError(ex, "MonitoringEventProcessor: Failed to process or persist monitoring event. EventType: {EventType}, Source: {Source}",
                    @event?.EventType ?? "Unknown", @event?.Source ?? "Unknown");
            }
        }

        _healthRegistry.RecordHeartbeat(nameof(MonitoringEventProcessor), "Stopped");
        _logger.LogInformation("MonitoringEventProcessor: Stopped.");
    }
}
