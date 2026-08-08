using System;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.Entities;

public class PositionEvent
{
    public Guid Id { get; private set; }
    public Guid PositionId { get; private set; }
    public string EventType { get; private set; }
    public string Payload { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Required for EF Core
    private PositionEvent()
    {
        Id = Guid.Empty;
        PositionId = Guid.Empty;
        EventType = string.Empty;
        Payload = string.Empty;
        CreatedAt = DateTime.UtcNow;
    }

    public PositionEvent(Guid positionId, string eventType, string payload)
    {
        if (positionId == Guid.Empty)
        {
            throw new DomainException("PositionId cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new DomainException("EventType cannot be empty.");
        }

        Id = Guid.Empty; // Let EF Core generate the Guid automatically on Add to prevent tracking conflicts
        PositionId = positionId;
        EventType = eventType;
        Payload = payload ?? string.Empty;
        CreatedAt = DateTime.UtcNow;
    }
}
