using System;

namespace TradingBot.Application.Dashboard.DTOs;

public sealed record WorkerStatusDto(
    string Name,
    string Status,
    DateTime? LastActivityAt,
    DateTime? LastSuccessfulExecutionAt,
    DateTime? LastFailureAt
);
