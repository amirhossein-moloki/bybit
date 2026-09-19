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
        IReadOnlyList<Notification> claimedNotifications;
        var workerId = Environment.MachineName ?? "NotificationWorker";

        // 1. Atomic claiming of notifications using PostgreSQL FOR UPDATE SKIP LOCKED
        using (var scope = _serviceProvider.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();
            claimedNotifications = await repository.ClaimPendingNotificationsAsync(
                batchSize: 20,
                workerId: workerId,
                cancellationToken: cancellationToken);
        }

        if (!claimedNotifications.Any())
        {
            return;
        }

        foreach (var notification in claimedNotifications)
        {
            _logger.LogInformation("Notification claimed:\nId={NotificationId}\nWorker={WorkerId}", notification.Id, workerId);
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

            // 3. Atomically save delivery state result
            using (var scope = _serviceProvider.CreateScope())
            {
                var repository = scope.ServiceProvider.GetRequiredService<INotificationRepository>();

                var updated = await repository.TryUpdateDeliveryResultAsync(
                    notificationId: notification.Id,
                    result: result,
                    initialRetryDelaySeconds: _options.Telegram?.InitialRetryDelaySeconds ?? 2,
                    maxRetryDelaySeconds: _options.Telegram?.MaxRetryDelaySeconds ?? 60,
                    cancellationToken: cancellationToken);

                if (updated)
                {
                    var finalStatus = result.Success
                        ? NotificationStatus.Delivered
                        : (result.IsRetryable && notification.AttemptCount < notification.MaxAttempts
                            ? NotificationStatus.RetryScheduled
                            : NotificationStatus.Failed);

                    _logger.LogInformation("Notification delivery state updated:\nId={NotificationId}\nStatus={Status}",
                        notification.Id, finalStatus);
                }
                else
                {
                    _logger.LogWarning("Notification delivery update skipped:\nId={NotificationId}\nReason=AlreadyProcessed",
                        notification.Id);
                }
            }
        }
    }
}
