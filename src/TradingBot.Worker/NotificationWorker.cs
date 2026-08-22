using System;
using System.Collections.Generic;
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
using TradingBot.Domain.Enums;

namespace TradingBot.Worker;

public class NotificationWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly NotificationOptions _options;
    private readonly ILogger<NotificationWorker> _logger;
    private readonly IWorkerHealthRegistry _healthRegistry;
    private readonly IEnumerable<INotificationChannel> _channels;

    public NotificationWorker(
        IServiceProvider serviceProvider,
        NotificationOptions options,
        ILogger<NotificationWorker> logger,
        IWorkerHealthRegistry healthRegistry,
        IEnumerable<INotificationChannel> channels)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _healthRegistry = healthRegistry ?? throw new ArgumentNullException(nameof(healthRegistry));
        _channels = channels ?? throw new ArgumentNullException(nameof(channels));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // Let worker yield startup thread

        if (!_options.Enabled)
        {
            _logger.LogInformation("NotificationWorker: Disabled in configuration. Skipping worker registration.");
            return;
        }

        _healthRegistry.RegisterWorker(nameof(NotificationWorker), isCritical: false);
        _logger.LogInformation("NotificationWorker: Starting background worker...");

        while (!stoppingToken.IsCancellationRequested)
        {
            _healthRegistry.RecordHeartbeat(nameof(NotificationWorker), "Running");

            try
            {
                await ProcessNotificationsBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _healthRegistry.RecordHeartbeat(nameof(NotificationWorker), "Failed", ex.Message);
                _logger.LogError(ex, "NotificationWorker: Error encountered in main loop iteration.");
            }

            // Run check every 2 seconds (fast polling, can be customized)
            try
            {
                await Task.Delay(2000, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _healthRegistry.RecordHeartbeat(nameof(NotificationWorker), "Stopped");
        _logger.LogInformation("NotificationWorker: Stopped.");
    }

    private async Task ProcessNotificationsBatchAsync(CancellationToken cancellationToken)
    {
        List<Notification> claimedNotifications;

        // 1. Transactional/concurrency-safe claiming of Pending or RetryScheduled notifications in bounded batch
        using (var scope = _serviceProvider.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            var eligible = await repository.GetPendingAndRetryScheduledAsync(cancellationToken);
            var batch = eligible.Take(20).ToList();

            if (!batch.Any())
            {
                return;
            }

            claimedNotifications = new List<Notification>();

            foreach (var notification in batch)
            {
                try
                {
                    // Transition to Processing state (Atomic Claiming)
                    notification.MarkProcessing();
                    repository.Update(notification);
                    claimedNotifications.Add(notification);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "NotificationWorker: Failed to claim notification {NotificationId}.", notification.Id);
                }
            }

            if (claimedNotifications.Any())
            {
                try
                {
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex) when (ex.GetType().Name.Contains("DbUpdateConcurrencyException"))
                {
                    _logger.LogWarning(ex, "NotificationWorker: Concurrency conflict while claiming notification batch. Skipping batch iteration.");
                    return;
                }
            }
        }

        // 2. Deliver outside database transactional lock
        foreach (var notification in claimedNotifications)
        {
            if (cancellationToken.IsCancellationRequested) return;

            NotificationDeliveryResult result;
            var channel = _channels.FirstOrDefault(c => c.ChannelName.Equals(notification.Channel, StringComparison.OrdinalIgnoreCase));

            if (channel == null)
            {
                _logger.LogError("NotificationWorker: Notification channel '{Channel}' not found. Rejecting notification {NotificationId}.",
                    notification.Channel, notification.Id);
                result = NotificationDeliveryResult.AsFailure(isRetryable: false, "CHANNEL_NOT_FOUND", $"Channel '{notification.Channel}' is not configured.");
            }
            else
            {
                try
                {
                    result = await channel.SendAsync(notification, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "NotificationWorker: Unexpected error calling send on channel {Channel} for notification {NotificationId}.",
                        notification.Channel, notification.Id);
                    result = NotificationDeliveryResult.AsFailure(isRetryable: true, "UNEXPECTED_CHANNEL_ERROR", ex.Message);
                }
            }

            // 3. Update status based on delivery result
            using (var scope = _serviceProvider.CreateScope())
            {
                var repository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
                var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

                var persistedNotification = await repository.GetByIdAsync(notification.Id, cancellationToken);
                if (persistedNotification == null)
                {
                    _logger.LogError("NotificationWorker: Failed to reload notification {NotificationId} to save delivery result.", notification.Id);
                    continue;
                }

                if (result.Success)
                {
                    persistedNotification.MarkDelivered();
                    _logger.LogInformation("NotificationWorker: Notification {NotificationId} successfully delivered via {Channel}.",
                        persistedNotification.Id, persistedNotification.Channel);
                }
                else
                {
                    // Exponential backoff with jitter
                    if (result.IsRetryable && persistedNotification.AttemptCount < persistedNotification.MaxAttempts)
                    {
                        var baseDelay = _options.Telegram?.InitialRetryDelaySeconds ?? 2;
                        var maxDelay = _options.Telegram?.MaxRetryDelaySeconds ?? 60;
                        var backoffSeconds = baseDelay * Math.Pow(2, persistedNotification.AttemptCount - 1);

                        // Add Jitter +/- 20% using thread-safe Random.Shared
                        var jitter = (Random.Shared.NextDouble() * 0.4) - 0.2; // -0.2 to +0.2
                        backoffSeconds = backoffSeconds * (1 + jitter);

                        var finalDelaySeconds = Math.Min(backoffSeconds, maxDelay);
                        if (finalDelaySeconds < 1) finalDelaySeconds = 1;

                        var nextAttemptAt = DateTime.UtcNow.AddSeconds(finalDelaySeconds);

                        persistedNotification.ScheduleRetry(nextAttemptAt, result.SafeMessage ?? "Transient error");
                        _logger.LogWarning("NotificationWorker: Notification {NotificationId} failed transiently. Scheduled retry {Attempt}/{Max} at {NextAttemptAt} UTC.",
                            persistedNotification.Id, persistedNotification.AttemptCount, persistedNotification.MaxAttempts, nextAttemptAt);
                    }
                    else
                    {
                        persistedNotification.MarkFailed(result.SafeMessage ?? "Max attempts exceeded or permanent error.");
                        _logger.LogError("NotificationWorker: Notification {NotificationId} failed permanently: {FailureReason}",
                            persistedNotification.Id, persistedNotification.FailureReason);
                    }
                }

                // Add to history
                persistedNotification.AddDeliveryAttempt(
                    attemptNumber: persistedNotification.AttemptCount,
                    isSuccess: result.Success,
                    errorCode: result.ErrorCode,
                    errorMessage: result.SafeMessage
                );

                try
                {
                    repository.Update(persistedNotification);
                    await unitOfWork.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex) when (ex.GetType().Name.Contains("DbUpdateConcurrencyException"))
                {
                    _logger.LogWarning(ex, "NotificationWorker: Concurrency conflict when saving delivery result for notification {NotificationId}. Skipping.", notification.Id);
                }
            }
        }
    }
}
