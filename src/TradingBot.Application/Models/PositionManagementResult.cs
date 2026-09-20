using System;

namespace TradingBot.Application.Models;

public record PositionManagementResult(
    bool Success,
    string Status,
    string AuditReason,
    Guid? PositionId = null,
    string? Symbol = null,
    string? ExecutedAction = null
);
