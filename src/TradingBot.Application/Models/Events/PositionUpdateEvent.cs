using System;

namespace TradingBot.Application.Models.Events;

public record PositionUpdateEvent(
    string Symbol,
    decimal Size,
    decimal EntryPrice,
    string Side,
    decimal Leverage,
    DateTime Timestamp
);
