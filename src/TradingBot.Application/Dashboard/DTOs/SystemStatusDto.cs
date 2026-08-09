using System;

namespace TradingBot.Application.Dashboard.DTOs;

public sealed record SystemStatusDto(
    string ApplicationStatus,
    string Uptime,
    string Environment,
    DateTime CurrentTimestamp
);
