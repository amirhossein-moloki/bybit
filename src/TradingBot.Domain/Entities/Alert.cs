using System;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.Entities;

public class Alert
{
    public Guid Id { get; private set; }
    public string RuleId { get; private set; }
    public string AlertType { get; private set; }
    public string Type => AlertType; // getter for compatibility with "Type" terminology
    public string Severity { get; private set; }
    public string Status { get; private set; }
    public string Source { get; private set; }
    public string Component { get; private set; }
    public string Message { get; private set; }
    public string? Payload { get; private set; }
    public string? CorrelationId { get; private set; }
    public string DeduplicationKey { get; private set; }
    public DateTime TriggeredAt { get; private set; }
    public DateTime LastSeenAt { get; private set; }
    public DateTime? ResolvedAt { get; private set; }
    public int TriggerCount { get; private set; }
    public int NotificationCount { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // Required for EF Core
    private Alert()
    {
        Id = Guid.Empty;
        RuleId = string.Empty;
        AlertType = string.Empty;
        Severity = string.Empty;
        Status = string.Empty;
        Source = string.Empty;
        Component = string.Empty;
        Message = string.Empty;
        DeduplicationKey = string.Empty;
    }

    public Alert(
        string ruleId,
        string alertType,
        string severity,
        string status,
        string source,
        string component,
        string message,
        string deduplicationKey,
        string? payload = null,
        string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(ruleId))
            throw new DomainException("RuleId cannot be empty.");
        if (string.IsNullOrWhiteSpace(alertType))
            throw new DomainException("AlertType cannot be empty.");
        if (string.IsNullOrWhiteSpace(severity))
            throw new DomainException("Severity cannot be empty.");
        if (string.IsNullOrWhiteSpace(status))
            throw new DomainException("Status cannot be empty.");
        if (string.IsNullOrWhiteSpace(source))
            throw new DomainException("Source cannot be empty.");
        if (string.IsNullOrWhiteSpace(component))
            throw new DomainException("Component cannot be empty.");
        if (string.IsNullOrWhiteSpace(message))
            throw new DomainException("Message cannot be empty.");
        if (string.IsNullOrWhiteSpace(deduplicationKey))
            throw new DomainException("DeduplicationKey cannot be empty.");

        Id = Guid.NewGuid();
        RuleId = ruleId.Trim();
        AlertType = alertType.Trim();
        Severity = severity.Trim().ToUpperInvariant();
        Status = status.Trim();
        Source = source.Trim();
        Component = component.Trim();
        Message = message.Trim();
        DeduplicationKey = deduplicationKey.Trim();
        Payload = payload;
        CorrelationId = correlationId;
        TriggeredAt = DateTime.UtcNow;
        LastSeenAt = DateTime.UtcNow;
        TriggerCount = 1;
        NotificationCount = 0;
        CreatedAt = DateTime.UtcNow;
    }

    public void UpdateLastSeen(string message, string? payload, string? correlationId)
    {
        LastSeenAt = DateTime.UtcNow;
        TriggerCount++;
        Message = message.Trim();
        Payload = payload;
        CorrelationId = correlationId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void TransitionTo(string newStatus)
    {
        if (Status == newStatus) return;
        Status = newStatus;
        if (newStatus == "Resolved")
        {
            ResolvedAt = DateTime.UtcNow;
        }
        UpdatedAt = DateTime.UtcNow;
    }

    public void IncrementNotificationCount()
    {
        NotificationCount++;
        UpdatedAt = DateTime.UtcNow;
    }
}
