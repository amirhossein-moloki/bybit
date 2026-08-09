using System;

namespace TradingBot.Domain.Entities;

public class ProcessedEvent
{
    public Guid Id { get; private set; }
    public string EventId { get; private set; } = null!;
    public string EventType { get; private set; } = null!;
    public Guid? PositionId { get; private set; }
    public string? ExchangeOrderId { get; private set; }
    public DateTime ProcessedAt { get; private set; }

    // Required for EF Core
    private ProcessedEvent() { }

    public ProcessedEvent(string eventId, string eventType, Guid? positionId, string? exchangeOrderId)
    {
        if (string.IsNullOrWhiteSpace(eventId))
            throw new ArgumentException("EventId cannot be empty.", nameof(eventId));
        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("EventType cannot be empty.", nameof(eventType));

        Id = Guid.NewGuid();
        EventId = eventId;
        EventType = eventType;
        PositionId = positionId;
        ExchangeOrderId = exchangeOrderId;
        ProcessedAt = DateTime.UtcNow;
    }
}
