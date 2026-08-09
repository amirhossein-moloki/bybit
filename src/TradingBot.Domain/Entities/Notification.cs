using System;
using System.Collections.Generic;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.Entities;

public class Notification
{
    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public string EventType { get; private set; }
    public string Severity { get; private set; }
    public string Channel { get; private set; }
    public string Recipient { get; private set; }
    public string Title { get; private set; }
    public string Message { get; private set; }
    public string? Payload { get; private set; }
    public string? CorrelationId { get; private set; }
    public NotificationStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; }
    public DateTime? LastAttemptAt { get; private set; }
    public DateTime? NextAttemptAt { get; private set; }
    public DateTime? DeliveredAt { get; private set; }
    public DateTime? FailedAt { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public ICollection<NotificationDeliveryAttempt> DeliveryAttempts { get; private set; } = new List<NotificationDeliveryAttempt>();

    // Required for EF Core
    private Notification()
    {
        Id = Guid.Empty;
        EventId = Guid.Empty;
        EventType = string.Empty;
        Severity = string.Empty;
        Channel = string.Empty;
        Recipient = string.Empty;
        Title = string.Empty;
        Message = string.Empty;
        Status = NotificationStatus.Pending;
        CreatedAt = DateTime.UtcNow;
    }

    public Notification(
        Guid eventId,
        string eventType,
        string severity,
        string channel,
        string recipient,
        string title,
        string message,
        string? payload = null,
        string? correlationId = null,
        int maxAttempts = 3)
    {
        if (eventId == Guid.Empty)
            throw new DomainException("EventId cannot be empty.");
        if (string.IsNullOrWhiteSpace(eventType))
            throw new DomainException("EventType cannot be empty.");
        if (string.IsNullOrWhiteSpace(severity))
            throw new DomainException("Severity cannot be empty.");
        if (string.IsNullOrWhiteSpace(channel))
            throw new DomainException("Channel cannot be empty.");
        if (string.IsNullOrWhiteSpace(recipient))
            throw new DomainException("Recipient cannot be empty.");
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainException("Title cannot be empty.");
        if (string.IsNullOrWhiteSpace(message))
            throw new DomainException("Message cannot be empty.");
        if (maxAttempts <= 0)
            throw new DomainException("MaxAttempts must be greater than zero.");

        Id = Guid.NewGuid();
        EventId = eventId;
        EventType = eventType.Trim();
        Severity = severity.Trim().ToUpperInvariant();
        Channel = channel.Trim();
        Recipient = recipient.Trim();
        Title = title.Trim();
        Message = message; // Keep formatting/spaces intact
        Payload = payload;
        CorrelationId = correlationId;
        Status = NotificationStatus.Pending;
        AttemptCount = 0;
        MaxAttempts = maxAttempts;
        CreatedAt = DateTime.UtcNow;
    }

    public void TransitionTo(NotificationStatus newStatus)
    {
        if (Status == newStatus) return;

        bool isValid = Status switch
        {
            NotificationStatus.Pending => newStatus == NotificationStatus.Processing ||
                                          newStatus == NotificationStatus.Cancelled,

            NotificationStatus.Processing => newStatus == NotificationStatus.Delivered ||
                                             newStatus == NotificationStatus.RetryScheduled ||
                                             newStatus == NotificationStatus.Failed ||
                                             newStatus == NotificationStatus.Cancelled,

            NotificationStatus.RetryScheduled => newStatus == NotificationStatus.Processing ||
                                                 newStatus == NotificationStatus.Failed ||
                                                 newStatus == NotificationStatus.Cancelled,

            _ => false
        };

        if (!isValid)
        {
            throw new DomainException($"Invalid transition: Cannot change notification status from {Status} to {newStatus}.");
        }

        Status = newStatus;
        UpdatedAt = DateTime.UtcNow;
    }

    public void MarkProcessing()
    {
        TransitionTo(NotificationStatus.Processing);
        AttemptCount++;
        LastAttemptAt = DateTime.UtcNow;
    }

    public void MarkDelivered()
    {
        TransitionTo(NotificationStatus.Delivered);
        DeliveredAt = DateTime.UtcNow;
        NextAttemptAt = null;
    }

    public void ScheduleRetry(DateTime nextAttemptAt, string failureReason)
    {
        TransitionTo(NotificationStatus.RetryScheduled);
        NextAttemptAt = nextAttemptAt;
        FailureReason = failureReason;
    }

    public void MarkFailed(string failureReason)
    {
        TransitionTo(NotificationStatus.Failed);
        FailedAt = DateTime.UtcNow;
        NextAttemptAt = null;
        FailureReason = failureReason;
    }

    public void Cancel()
    {
        TransitionTo(NotificationStatus.Cancelled);
        NextAttemptAt = null;
    }

    public void AddDeliveryAttempt(int attemptNumber, bool isSuccess, string? errorCode, string? errorMessage)
    {
        var attempt = new NotificationDeliveryAttempt(Id, attemptNumber, isSuccess, errorCode, errorMessage);
        DeliveryAttempts.Add(attempt);
        UpdatedAt = DateTime.UtcNow;
    }
}
