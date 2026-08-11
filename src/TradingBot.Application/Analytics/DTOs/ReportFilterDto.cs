using System;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.Analytics.DTOs;

public sealed record ReportFilterDto(
    DateTime? StartDate = null,
    DateTime? EndDate = null,
    string? Symbol = null,
    OrderSide? Side = null,
    decimal? MinPnL = null,
    decimal? MaxPnL = null,
    CloseReason? CloseReason = null
);
