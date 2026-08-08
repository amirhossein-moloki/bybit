using System;
using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Entities;

public class OrderEvent
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public OrderStatus PreviousStatus { get; private set; }
    public OrderStatus NewStatus { get; private set; }
    public string EventType { get; private set; }
    public string Source { get; private set; }
    public string Message { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Required for EF Core
    private OrderEvent()
    {
        Id = Guid.Empty;
        OrderId = Guid.Empty;
        EventType = string.Empty;
        Source = string.Empty;
        Message = string.Empty;
        CreatedAt = DateTime.UtcNow;
    }

    public OrderEvent(Guid orderId, OrderStatus previousStatus, OrderStatus newStatus, string eventType, string source, string message)
    {
        Id = Guid.NewGuid();
        OrderId = orderId;
        PreviousStatus = previousStatus;
        NewStatus = newStatus;
        EventType = eventType ?? throw new ArgumentNullException(nameof(eventType));
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Message = message ?? throw new ArgumentNullException(nameof(message));
        CreatedAt = DateTime.UtcNow;
    }
}
