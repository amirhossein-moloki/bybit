using System;

namespace TradingBot.Application.Dashboard.DTOs;

public sealed record RecentEventDto(
    Guid Id,
    string Type,
    string Severity,
    string Source,
    DateTime Timestamp,
    string? CorrelationId,
    string Message
);
