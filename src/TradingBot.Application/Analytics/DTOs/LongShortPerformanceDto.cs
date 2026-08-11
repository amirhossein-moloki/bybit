using System;

namespace TradingBot.Application.Analytics.DTOs;

public sealed record LongShortPerformanceDto(
    SidePerformanceDto Long,
    SidePerformanceDto Short
);
