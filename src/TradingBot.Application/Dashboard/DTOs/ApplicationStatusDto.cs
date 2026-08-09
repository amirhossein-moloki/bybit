using System;

namespace TradingBot.Application.Dashboard.DTOs;

public sealed record ApplicationStatusDto(
    string Status,
    string Uptime,
    DateTime StartedAt,
    DateTime CurrentTimestamp,
    string Environment
);
