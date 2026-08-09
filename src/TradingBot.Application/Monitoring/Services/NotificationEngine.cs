using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Monitoring.Configuration;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Monitoring.Services;

public class NotificationEngine : INotificationEngine
{
    private readonly INotificationPolicy _policy;
    private readonly ITelegramMessageBuilder _messageBuilder;
    private readonly INotificationRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly NotificationOptions _options;
    private readonly ILogger<NotificationEngine> _logger;

    public NotificationEngine(
        INotificationPolicy policy,
        ITelegramMessageBuilder messageBuilder,
        INotificationRepository repository,
        IUnitOfWork unitOfWork,
        NotificationOptions options,
        ILogger<NotificationEngine> logger)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _messageBuilder = messageBuilder ?? throw new ArgumentNullException(nameof(messageBuilder));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task ProcessEventAsync(MonitoringEvent @event, CancellationToken cancellationToken = default)
    {
        try
        {
            if (@event == null) return;

            // 1. Evaluate policy
            if (!_policy.ShouldNotify(@event))
            {
                _logger.LogDebug("NotificationEngine: Event {EventType} with severity {Severity} is not eligible for notification.",
                    @event.EventType, @event.Severity);
                return;
            }

            var channel = "Telegram";
            var recipient = _options.Telegram?.ChatId ?? string.Empty;

            if (string.IsNullOrWhiteSpace(recipient))
            {
                _logger.LogWarning("NotificationEngine: Telegram notifications are enabled but ChatId is not configured.");
                return;
            }

            // 2. Check for duplicate to enforce idempotency foundation
            var exists = await _repository.ExistsForEventAsync(@event.Id, channel, recipient, cancellationToken);
            if (exists)
            {
                _logger.LogDebug("NotificationEngine: Notification already exists for EventId {EventId}, Channel {Channel}, Recipient {Recipient}.",
                    @event.Id, channel, recipient);
                return;
            }

            // 3. Convert event into beautiful notification message
            var message = _messageBuilder.BuildMessage(@event);
            var title = @event.EventType;

            // 4. Create Notification entity
            var notification = new Notification(
                eventId: @event.Id,
                eventType: @event.EventType,
                severity: @event.Severity,
                channel: channel,
                recipient: recipient,
                title: title,
                message: message,
                payload: @event.Payload,
                correlationId: @event.CorrelationId,
                maxAttempts: _options.Telegram?.RetryCount ?? 3
            );

            // 5. Persist the notification
            await _repository.AddAsync(notification, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("NotificationEngine: Created and persisted notification {NotificationId} for Event {EventType} (CorrelationId: {CorrelationId}).",
                notification.Id, @event.EventType, @event.CorrelationId);
        }
        catch (Exception ex)
        {
            // Section 37: Isolation ensures notification failures NEVER crash the system or stop processing loops
            _logger.LogError(ex, "NotificationEngine: Failed to process monitoring event for notification. EventType: {EventType}, EventId: {EventId}",
                @event?.EventType ?? "Unknown", @event?.Id ?? Guid.Empty);
        }
    }
}
