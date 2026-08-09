using System;

namespace TradingBot.Application.Dashboard.DTOs;

public sealed record AlertDto(
    Guid Id,
    string Type,
    string Severity,
    string Source,
    string Status,
    string Message,
    DateTime TriggeredAt,
    DateTime? LastUpdatedAt,
    string? CorrelationId
);
