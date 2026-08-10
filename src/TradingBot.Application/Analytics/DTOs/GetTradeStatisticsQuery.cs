using System;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Analytics.DTOs;

public sealed record GetTradeStatisticsQuery(
    DateTime? From = null,
    DateTime? To = null,
    string? Symbol = null,
    OrderSide? Side = null
);
