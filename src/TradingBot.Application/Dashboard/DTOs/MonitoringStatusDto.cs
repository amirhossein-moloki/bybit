using System;

namespace TradingBot.Application.Dashboard.DTOs;

public sealed record MonitoringStatusDto(
    string MonitoringStatus,
    DateTime? LastSuccessfulCycle,
    DateTime? LastFailure
);
