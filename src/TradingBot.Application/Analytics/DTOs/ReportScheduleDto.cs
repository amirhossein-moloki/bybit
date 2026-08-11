using System;

namespace TradingBot.Application.Analytics.DTOs;

public sealed record ReportScheduleDto(
    Guid? Id,
    string ScheduleName,
    string CronExpression,
    string ReportType,
    string EmailRecipient,
    string ExportFormat,
    bool IsActive = true
);
