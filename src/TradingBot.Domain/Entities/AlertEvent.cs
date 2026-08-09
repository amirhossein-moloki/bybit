using System;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.Entities;

public class AlertEvent
{
    public Guid Id { get; private set; }
    public Guid AlertId { get; private set; }
    public string EventType { get; private set; }
    public string PreviousStatus { get; private set; }
    public string NewStatus { get; private set; }
    public string? Payload { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Required for EF Core
    private AlertEvent()
    {
        Id = Guid.Empty;
        AlertId = Guid.Empty;
        EventType = string.Empty;
        PreviousStatus = string.Empty;
        NewStatus = string.Empty;
    }

    public AlertEvent(
        Guid alertId,
        string eventType,
        string previousStatus,
        string newStatus,
        string? payload = null,
        string? correlationId = null)
    {
        if (alertId == Guid.Empty)
            throw new DomainException("AlertId cannot be empty.");
        if (string.IsNullOrWhiteSpace(eventType))
            throw new DomainException("EventType cannot be empty.");
        if (string.IsNullOrWhiteSpace(previousStatus))
            throw new DomainException("PreviousStatus cannot be empty.");
        if (string.IsNullOrWhiteSpace(newStatus))
            throw new DomainException("NewStatus cannot be empty.");

        Id = Guid.NewGuid();
        AlertId = alertId;
        EventType = eventType;
        PreviousStatus = previousStatus;
        NewStatus = newStatus;
        Payload = payload;
        CorrelationId = correlationId;
        CreatedAt = DateTime.UtcNow;
    }
}
