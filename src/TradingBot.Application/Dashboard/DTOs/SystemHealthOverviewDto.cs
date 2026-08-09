using System;
using System.Collections.Generic;

namespace TradingBot.Application.Dashboard.DTOs;

public sealed record SystemHealthOverviewDto(
    string OverallStatus,
    ApplicationStatusDto Application,
    DatabaseHealthDto Database,
    BybitHealthDto Bybit,
    TelegramHealthDto Telegram,
    IReadOnlyList<WorkerStatusDto> Workers,
    MonitoringStatusDto Monitoring,
    AlertSummaryDto AlertSummary,
    IReadOnlyList<AlertDto> ActiveAlerts,
    IReadOnlyList<RecentEventDto> RecentEvents,
    IReadOnlyList<HealthHistoryRecordDto> HealthHistory,
    OperationalMetricsDto Metrics
);
