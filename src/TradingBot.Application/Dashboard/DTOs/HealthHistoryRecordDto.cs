using System;

namespace TradingBot.Application.Dashboard.DTOs;

public sealed record HealthHistoryRecordDto(
    string Service,
    string Status,
    DateTime CheckedAt,
    long ResponseTime
);
