using System;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Models.Events;

public record OrderUpdateEvent(
    string ClientOrderId,
    string ExchangeOrderId,
    string Symbol,
    OrderStatus Status,
    decimal Price,
    decimal Quantity,
    decimal FilledQuantity,
    string? RejectReason,
    DateTime Timestamp
);
