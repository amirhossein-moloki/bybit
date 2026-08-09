using System;
using TradingBot.Domain.Enums;

namespace TradingBot.Domain.Entities;

public class HealthCheckResult
{
    public Guid Id { get; private set; }
    public string ServiceName { get; private set; } = string.Empty;
    public HealthStatus Status { get; private set; }
    public DateTime CheckedAt { get; private set; }
    public long DurationMs { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? Metadata { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Required for EF Core
    private HealthCheckResult()
    {
        Id = Guid.Empty;
        ServiceName = string.Empty;
        Status = HealthStatus.Unknown;
        CheckedAt = DateTime.UtcNow;
        CreatedAt = DateTime.UtcNow;
    }

    public HealthCheckResult(
        string serviceName,
        HealthStatus status,
        DateTime checkedAt,
        long durationMs,
        string? errorCode = null,
        string? errorMessage = null,
        string? metadata = null)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
        {
            throw new ArgumentException("Service name cannot be null or empty.", nameof(serviceName));
        }

        Id = Guid.NewGuid();
        ServiceName = serviceName.Trim();
        Status = status;
        CheckedAt = checkedAt;
        DurationMs = durationMs;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Metadata = metadata;
        CreatedAt = DateTime.UtcNow;
    }
}
