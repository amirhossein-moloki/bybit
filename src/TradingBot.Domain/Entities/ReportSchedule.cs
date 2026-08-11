using System;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.Entities;

public class ReportSchedule
{
    public Guid Id { get; private set; }
    public string ScheduleName { get; private set; }
    public string CronExpression { get; private set; }
    public string ReportType { get; private set; }
    public string EmailRecipient { get; private set; }
    public string ExportFormat { get; private set; }
    public bool IsActive { get; private set; }

    // Required for EF Core
    private ReportSchedule()
    {
        Id = Guid.Empty;
        ScheduleName = string.Empty;
        CronExpression = string.Empty;
        ReportType = string.Empty;
        EmailRecipient = string.Empty;
        ExportFormat = string.Empty;
    }

    public ReportSchedule(
        string scheduleName,
        string cronExpression,
        string reportType,
        string emailRecipient,
        string exportFormat,
        bool isActive = true)
    {
        if (string.IsNullOrWhiteSpace(scheduleName))
        {
            throw new DomainException("ScheduleName cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(cronExpression))
        {
            throw new DomainException("CronExpression cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(reportType))
        {
            throw new DomainException("ReportType cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(emailRecipient))
        {
            throw new DomainException("EmailRecipient cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(exportFormat))
        {
            throw new DomainException("ExportFormat cannot be empty.");
        }

        Id = Guid.NewGuid();
        ScheduleName = scheduleName.Trim();
        CronExpression = cronExpression.Trim();
        ReportType = reportType.Trim();
        EmailRecipient = emailRecipient.Trim();
        ExportFormat = exportFormat.Trim();
        IsActive = isActive;
    }

    public void UpdateSchedule(
        string scheduleName,
        string cronExpression,
        string reportType,
        string emailRecipient,
        string exportFormat,
        bool isActive)
    {
        if (string.IsNullOrWhiteSpace(scheduleName))
        {
            throw new DomainException("ScheduleName cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(cronExpression))
        {
            throw new DomainException("CronExpression cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(reportType))
        {
            throw new DomainException("ReportType cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(emailRecipient))
        {
            throw new DomainException("EmailRecipient cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(exportFormat))
        {
            throw new DomainException("ExportFormat cannot be empty.");
        }

        ScheduleName = scheduleName.Trim();
        CronExpression = cronExpression.Trim();
        ReportType = reportType.Trim();
        EmailRecipient = emailRecipient.Trim();
        ExportFormat = exportFormat.Trim();
        IsActive = isActive;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void Activate()
    {
        IsActive = true;
    }
}
