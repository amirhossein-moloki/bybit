using System;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Dashboard.DTOs;

public sealed record TradingDashboardQuery(
    string? Symbol = null,
    OrderSide? Side = null,
    string? Status = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 50
);
