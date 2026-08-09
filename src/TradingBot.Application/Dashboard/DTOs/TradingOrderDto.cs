using System;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Dashboard.DTOs;

public sealed record TradingOrderDto(
    Guid Id,
    string Symbol,
    OrderSide Side,
    OrderType Type,
    decimal Quantity,
    decimal Price,
    OrderStatus Status,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);
