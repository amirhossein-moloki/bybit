using System;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.Entities;

public class MonitoringEvent
{
    public Guid Id { get; private set; }
    public string EventType { get; private set; }
    public string Severity { get; private set; }
    public string Source { get; private set; }
    public string Component { get; private set; }
    public string Status { get; private set; }
    public string Message { get; private set; }

    public string? CorrelationId { get; private set; }
    public string? OperationId { get; private set; }

    public Guid? SignalId { get; private set; }
    public Guid? OrderId { get; private set; }
    public Guid? PositionId { get; private set; }

    public string? Payload { get; private set; }

    public string? ErrorCode { get; private set; }
    public string? ExceptionType { get; private set; }

    public string? ExternalEventId { get; private set; }

    public DateTime Timestamp { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Required for EF Core
    private MonitoringEvent()
    {
        Id = Guid.Empty;
        EventType = string.Empty;
        Severity = string.Empty;
        Source = string.Empty;
        Component = string.Empty;
        Status = string.Empty;
        Message = string.Empty;
        Timestamp = DateTime.UtcNow;
        CreatedAt = DateTime.UtcNow;
    }

    public MonitoringEvent(
        string eventType,
        string severity,
        string source,
        string component,
        string status,
        string message,
        string? correlationId = null,
        string? operationId = null,
        Guid? signalId = null,
        Guid? orderId = null,
        Guid? positionId = null,
        string? payload = null,
        string? errorCode = null,
        string? exceptionType = null,
        string? externalEventId = null,
        DateTime? timestamp = null)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            throw new DomainException("EventType cannot be empty.");
        if (string.IsNullOrWhiteSpace(severity))
            throw new DomainException("Severity cannot be empty.");
        if (string.IsNullOrWhiteSpace(source))
            throw new DomainException("Source cannot be empty.");
        if (string.IsNullOrWhiteSpace(component))
            throw new DomainException("Component cannot be empty.");
        if (string.IsNullOrWhiteSpace(status))
            throw new DomainException("Status cannot be empty.");
        if (string.IsNullOrWhiteSpace(message))
            throw new DomainException("Message cannot be empty.");

        Id = Guid.NewGuid();
        EventType = eventType.Trim();
        Severity = severity.Trim().ToUpperInvariant();
        Source = source.Trim();
        Component = component.Trim();
        Status = status.Trim();
        Message = message.Trim();
        CorrelationId = correlationId;
        OperationId = operationId;
        SignalId = signalId;
        OrderId = orderId;
        PositionId = positionId;
        Payload = payload;
        ErrorCode = errorCode;
        ExceptionType = exceptionType;
        ExternalEventId = externalEventId;
        Timestamp = timestamp ?? DateTime.UtcNow;
        CreatedAt = DateTime.UtcNow;
    }
}
