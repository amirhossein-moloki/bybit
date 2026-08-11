using System;

namespace TradingBot.Application.Analytics.DTOs;

public sealed record GetAnalyticsQuery(
    DateTime? StartDate = null,
    DateTime? EndDate = null,
    string? Symbol = null,
    decimal? InitialBalance = null
);
