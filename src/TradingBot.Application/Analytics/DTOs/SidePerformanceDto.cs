using System;

namespace TradingBot.Application.Analytics.DTOs;

public sealed record SidePerformanceDto(
    int Trades,
    int Wins,
    int Losses,
    decimal WinRate,
    decimal TotalPnL,
    decimal AveragePnL
);
