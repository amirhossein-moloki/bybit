using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Monitoring.Configuration;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Monitoring.Services;

public class AlertEngine : IAlertEngine
{
    private readonly IServiceProvider _serviceProvider;
    private readonly AlertOptions _options;
    private readonly ILogger<AlertEngine> _logger;

    // Thread-safe in-memory state tracking
    private static readonly ConcurrentDictionary<string, DateTime> LastNotificationTimes = new();
    private static readonly ConcurrentDictionary<string, DateTime> ConditionStartedTimes = new();

    public AlertEngine(
        IServiceProvider serviceProvider,
        AlertOptions options,
        ILogger<AlertEngine> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> ProcessEventAsync(MonitoringEvent @event, CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return true; // Handled (but disabled)
        if (@event == null) return false;

        // Skip alerting on alert events themselves to avoid loops
        if (@event.EventType.StartsWith("Alert", StringComparison.OrdinalIgnoreCase)) return true;

        try
        {
            // 1. Is this a recovery event?
            bool isRecovery = IsRecoveryEvent(@event);
            if (isRecovery)
            {
                return await HandleRecoveryEventAsync(@event, cancellationToken);
            }

            bool matchedAny = false;
            // 2. Match against configured rules
            foreach (var ruleEntry in _options.Rules)
            {
                var ruleKey = ruleEntry.Key;
                var ruleSettings = ruleEntry.Value;

                if (!ruleSettings.Enabled) continue;

                // Match based on event type or rule key matching event type, and optionally component
                bool matchesType = string.Equals(ruleSettings.EventType, @event.EventType, StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(ruleKey, @event.EventType, StringComparison.OrdinalIgnoreCase);

                bool matchesComponent = string.IsNullOrWhiteSpace(ruleSettings.Component) ||
                                        string.Equals(ruleSettings.Component, @event.Component, StringComparison.OrdinalIgnoreCase);

                if (matchesType && matchesComponent)
                {
                    matchedAny = true;
                    await EvaluateRuleAsync(ruleKey, ruleSettings, @event, cancellationToken);
                }
            }

            return matchedAny;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AlertEngine: Error processing monitoring event {@EventId}", @event.Id);
            return false;
        }
    }

    private bool IsRecoveryEvent(MonitoringEvent @event)
    {
        var type = @event.EventType.ToLowerInvariant();
        var msg = @event.Message.ToLowerInvariant();

        if (type.Contains("disconnected") || msg.Contains("disconnected") || msg.Contains("lost") || msg.Contains("fail"))
        {
            return false;
        }

        return type.Contains("restored") ||
               type.Contains("connected") ||
               type.Contains("recovered") ||
               msg.Contains("restored") ||
               msg.Contains("connected") ||
               msg.Contains("healthy") ||
               msg.Contains("recovery");
    }

    private async Task<bool> HandleRecoveryEventAsync(MonitoringEvent @event, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var alertRepo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
        var alertEventRepo = scope.ServiceProvider.GetRequiredService<IAlertEventRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var metricsService = scope.ServiceProvider.GetService<IMetricsService>();

        // Find active alerts with matching component
        var activeAlerts = await alertRepo.GetActiveAlertsAsync(cancellationToken);
        var alertsToResolve = activeAlerts.Where(a =>
            string.Equals(a.Component, @event.Component, StringComparison.OrdinalIgnoreCase) ||
            @event.Message.Contains(a.Component, StringComparison.OrdinalIgnoreCase)
        ).ToList();

        foreach (var alert in alertsToResolve)
        {
            var oldStatus = alert.Status;
            alert.TransitionTo("Resolved");
            alertRepo.Update(alert);

            var alertEvent = new AlertEvent(
                alert.Id,
                "Resolved",
                oldStatus,
                "Resolved",
                @event.Payload,
                @event.CorrelationId
            );
            await alertEventRepo.AddAsync(alertEvent, cancellationToken);

            metricsService?.IncrementAlertsResolved();

            _logger.LogInformation("AlertEngine: Resolved Alert {AlertId} for Rule {RuleId} on Component {Component}.",
                alert.Id, alert.RuleId, alert.Component);

            // Send recovery notification
            await SendNotificationAsync(alert, $"🟢 Connection/Condition Restored for {alert.Component}. Alert Resolved.", cancellationToken);
        }

        if (alertsToResolve.Any())
        {
            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (Exception ex) when (ex.GetType().Name == "DbUpdateConcurrencyException")
            {
                _logger.LogInformation("AlertEngine: Concurrency conflict resolving alerts for component {Component}. State is already resolved.", @event.Component);
                return true;
            }
        }

        return false;
    }

    private async Task EvaluateRuleAsync(string ruleKey, AlertRuleSettings ruleSettings, MonitoringEvent @event, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var alertRepo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
        var alertEventRepo = scope.ServiceProvider.GetRequiredService<IAlertEventRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var metricsService = scope.ServiceProvider.GetService<IMetricsService>();

        var resourceId = @event.OrderId?.ToString() ?? @event.PositionId?.ToString() ?? @event.SignalId?.ToString();
        var deduplicationKey = $"{ruleKey}:{@event.Component}:{@event.EventType}:{resourceId ?? ""}".Trim(':');

        var activeAlert = await alertRepo.GetActiveByDeduplicationKeyAsync(deduplicationKey, cancellationToken);

        if (activeAlert != null)
        {
            // 1. Deduplication: update existing alert
            var oldStatus = activeAlert.Status;
            activeAlert.UpdateLastSeen(@event.Message, @event.Payload, @event.CorrelationId);
            alertRepo.Update(activeAlert);

            var alertEvent = new AlertEvent(
                activeAlert.Id,
                "Repeated",
                oldStatus,
                activeAlert.Status,
                @event.Payload,
                @event.CorrelationId
            );
            await alertEventRepo.AddAsync(alertEvent, cancellationToken);

            metricsService?.IncrementAlertsDeduplicated();

            _logger.LogDebug("AlertEngine: Deduplicated event for Rule {RuleKey}. Trigger count: {Count}.",
                ruleKey, activeAlert.TriggerCount);

            // Handle Cooldown / Suppressed state / Repeat Notification Interval
            if (activeAlert.Status == "Suppressed")
            {
                metricsService?.IncrementNotificationsSuppressed();
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return;
            }

            bool shouldNotify = true;
            var cooldown = ruleSettings.GetCooldownTimeSpan();
            var repeatInterval = ruleSettings.GetRepeatNotificationIntervalTimeSpan();

            LastNotificationTimes.TryGetValue(deduplicationKey, out var lastNotifiedAt);

            // Startup recovery for last notification time fallback
            if (lastNotifiedAt == default && activeAlert.NotificationCount > 0)
            {
                lastNotifiedAt = activeAlert.UpdatedAt ?? activeAlert.TriggeredAt;
            }

            if (lastNotifiedAt != default)
            {
                var elapsedSinceLastNotification = DateTime.UtcNow - lastNotifiedAt;

                if (cooldown.HasValue && elapsedSinceLastNotification < cooldown.Value)
                {
                    shouldNotify = false;
                    _logger.LogDebug("AlertEngine: Notification suppressed due to Cooldown of {Cooldown} for DeduplicationKey {Key}.",
                        ruleSettings.Cooldown, deduplicationKey);
                    metricsService?.IncrementNotificationsSuppressed();
                }
                else if (repeatInterval.HasValue)
                {
                    if (elapsedSinceLastNotification >= repeatInterval.Value)
                    {
                        shouldNotify = true;
                        _logger.LogInformation("AlertEngine: Sending repeat notification for DeduplicationKey {Key} after {Interval}.",
                            deduplicationKey, ruleSettings.RepeatNotificationInterval);
                    }
                    else
                    {
                        shouldNotify = false;
                    }
                }
                else if (cooldown.HasValue)
                {
                    // If cooldown elapsed, we can notify again
                    shouldNotify = true;
                }
                else
                {
                    // No cooldown or repeat configured, do not storm notifications
                    shouldNotify = false;
                }
            }

            if (shouldNotify)
            {
                activeAlert.IncrementNotificationCount();
                alertRepo.Update(activeAlert);
                LastNotificationTimes[deduplicationKey] = DateTime.UtcNow;

                await SendNotificationAsync(activeAlert, $"⚠️ Repeated Alert: {activeAlert.Message} (Count: {activeAlert.TriggerCount})", cancellationToken);
            }

            // Concurrency retry loop for trigger count update
            int retryCount = 3;
            while (retryCount > 0)
            {
                try
                {
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                    break;
                }
                catch (Exception ex)
                {
                    var typeName = ex.GetType().Name;
                    if (typeName == "DbUpdateConcurrencyException" || typeName.Contains("DbUpdateConcurrencyException"))
                    {
                        retryCount--;
                        if (retryCount == 0)
                        {
                            _logger.LogWarning("AlertEngine: Concurrency conflict updating active alert trigger count. Max retries reached.");
                            break;
                        }

                        _logger.LogInformation("AlertEngine: Concurrency conflict updating active alert trigger count. Retrying...");
                        // Reload alert from database to get latest concurrency token and values
                        activeAlert = await alertRepo.GetActiveByDeduplicationKeyAsync(deduplicationKey, cancellationToken);
                        if (activeAlert == null) break; // Already resolved or deleted

                        activeAlert.UpdateLastSeen(@event.Message, @event.Payload, @event.CorrelationId);
                        alertRepo.Update(activeAlert);
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }
        else
        {
            // 2. Newly detected condition
            var threshold = ruleSettings.GetThresholdTimeSpan();
            string initialStatus = threshold.HasValue ? "Inactive" : "Triggered";

            var alert = new TradingBot.Domain.Entities.Alert(
                ruleId: ruleKey,
                alertType: @event.EventType,
                severity: ruleSettings.Severity,
                status: initialStatus,
                source: @event.Source,
                component: @event.Component,
                message: @event.Message,
                deduplicationKey: deduplicationKey,
                payload: @event.Payload,
                correlationId: @event.CorrelationId
            );

            bool alertSaved = false;
            try
            {
                await alertRepo.AddAsync(alert, cancellationToken);
                await unitOfWork.SaveChangesAsync(cancellationToken); // Save to get AlertId
                alertSaved = true;
            }
            catch (Exception ex)
            {
                var typeName = ex.GetType().Name;
                bool isDbUpdate = typeName == "DbUpdateException" || typeName.Contains("DbUpdateException");
                bool isUniqueConstraint = ex.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) ||
                                          ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true ||
                                          ex.InnerException?.Message.Contains("DeduplicationKey", StringComparison.OrdinalIgnoreCase) == true ||
                                          ex.InnerException?.Message.Contains("SQLite Error 19", StringComparison.OrdinalIgnoreCase) == true;

                if (isDbUpdate && isUniqueConstraint)
                {
                    _logger.LogInformation("AlertEngine: Concurrent alert creation detected for DeduplicationKey {Key}. Falling back to existing alert.", deduplicationKey);
                    // Try to load the active alert that was just created by the competing thread
                    var existing = await alertRepo.GetActiveByDeduplicationKeyAsync(deduplicationKey, cancellationToken);
                    if (existing != null)
                    {
                        existing.UpdateLastSeen(@event.Message, @event.Payload, @event.CorrelationId);
                        alertRepo.Update(existing);
                        await unitOfWork.SaveChangesAsync(cancellationToken);
                    }
                    return;
                }

                throw;
            }

            if (alertSaved)
            {
                var alertEvent = new AlertEvent(
                    alert.Id,
                    "Created",
                    "None",
                    initialStatus,
                    @event.Payload,
                    @event.CorrelationId
                );
                await alertEventRepo.AddAsync(alertEvent, cancellationToken);

                if (initialStatus == "Triggered")
                {
                    metricsService?.IncrementAlertsTriggered();
                    alert.IncrementNotificationCount();
                    alertRepo.Update(alert);
                    LastNotificationTimes[deduplicationKey] = DateTime.UtcNow;

                    await SendNotificationAsync(alert, $"⚠️ Alert Triggered: {alert.Message}", cancellationToken);
                }
                else
                {
                    // Inactive state - track start time for threshold
                    ConditionStartedTimes[deduplicationKey] = DateTime.UtcNow;
                    _logger.LogInformation("AlertEngine: Time-based rule {RuleKey} condition started. Waiting for {Threshold} threshold.",
                        ruleKey, ruleSettings.Threshold);
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
        }
    }

    public async Task EvaluateActiveAlertsAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled) return;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var alertRepo = scope.ServiceProvider.GetRequiredService<IAlertRepository>();
            var alertEventRepo = scope.ServiceProvider.GetRequiredService<IAlertEventRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var metricsService = scope.ServiceProvider.GetService<IMetricsService>();

            var activeAlerts = await alertRepo.GetActiveAlertsAsync(cancellationToken);

            bool changed = false;

            foreach (var alert in activeAlerts)
            {
                if (alert.Status == "Inactive")
                {
                    // Check if time-based threshold has been reached
                    _options.Rules.TryGetValue(alert.RuleId, out var ruleSettings);
                    if (ruleSettings != null)
                    {
                        var threshold = ruleSettings.GetThresholdTimeSpan();
                        ConditionStartedTimes.TryGetValue(alert.DeduplicationKey, out var startedAt);

                        if (threshold.HasValue)
                        {
                            var actualStartedAt = startedAt != default ? startedAt : alert.TriggeredAt;
                            if (startedAt == default)
                            {
                                ConditionStartedTimes[alert.DeduplicationKey] = alert.TriggeredAt;
                            }

                            if (DateTime.UtcNow - actualStartedAt >= threshold.Value)
                            {
                                // Transition to Triggered!
                                var oldStatus = alert.Status;
                                alert.TransitionTo("Triggered");
                                alert.IncrementNotificationCount();
                                alertRepo.Update(alert);

                                var alertEvent = new AlertEvent(
                                    alert.Id,
                                    "Triggered",
                                    oldStatus,
                                    "Triggered",
                                    alert.Payload,
                                    alert.CorrelationId
                                );
                                await alertEventRepo.AddAsync(alertEvent, cancellationToken);

                                metricsService?.IncrementAlertsTriggered();
                                LastNotificationTimes[alert.DeduplicationKey] = DateTime.UtcNow;

                                _logger.LogWarning("AlertEngine: Time-based Rule {RuleId} threshold of {Threshold} reached. Triggering Alert.",
                                    alert.RuleId, ruleSettings.Threshold);

                                await SendNotificationAsync(alert, $"⚠️ Alert Triggered: {alert.Message}", cancellationToken);
                                changed = true;
                            }
                        }
                    }
                }
            }

            if (changed)
            {
                try
                {
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex) when (ex.GetType().Name == "DbUpdateConcurrencyException")
                {
                    _logger.LogInformation("AlertEngine: Concurrency conflict evaluating active alerts. State is likely updated concurrently.");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AlertEngine: Error evaluating active time-based alerts.");
        }
    }

    private async Task SendNotificationAsync(TradingBot.Domain.Entities.Alert alert, string formattedMessage, CancellationToken cancellationToken)
    {
        try
        {
            // Build the Alert Notification event to propagate to NotificationEngine
            var alertEvent = new MonitoringEvent(
                eventType: "AlertEvent",
                severity: alert.Severity,
                source: alert.Source,
                component: alert.Component,
                status: alert.Status,
                message: formattedMessage,
                correlationId: alert.CorrelationId,
                payload: alert.Payload
            );

            using var scope = _serviceProvider.CreateScope();
            var notificationEngine = scope.ServiceProvider.GetRequiredService<INotificationEngine>();
            var metricsService = scope.ServiceProvider.GetService<IMetricsService>();

            metricsService?.IncrementNotificationsCreated();

            await notificationEngine.ProcessEventAsync(alertEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AlertEngine: Failed to send notification for Alert {AlertId}.", alert.Id);
        }
    }
}
