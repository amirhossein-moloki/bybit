using System;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.Entities;

public class SystemLog
{
    public Guid Id { get; private set; }
    public string Level { get; private set; }
    public string Category { get; private set; }
    public string Message { get; private set; }
    public string? Exception { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Required for EF Core
    private SystemLog()
    {
        Id = Guid.Empty;
        Level = string.Empty;
        Category = string.Empty;
        Message = string.Empty;
        CreatedAt = DateTime.UtcNow;
    }

    public SystemLog(string level, string category, string message, string? exception = null)
    {
        if (string.IsNullOrWhiteSpace(level))
        {
            throw new DomainException("LogLevel cannot be null or empty.");
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            throw new DomainException("Category cannot be null or empty.");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new DomainException("Message cannot be null or empty.");
        }

        Id = Guid.NewGuid();
        Level = level.Trim().ToUpperInvariant();
        Category = category.Trim();
        Message = message.Trim();
        Exception = exception;
        CreatedAt = DateTime.UtcNow;
    }

    public static SystemLog CreateAuditLog(string level, string operationName, string entityType, string entityId, string description)
    {
        if (string.IsNullOrWhiteSpace(operationName))
        {
            throw new DomainException("OperationName cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new DomainException("EntityType cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(entityId))
        {
            throw new DomainException("EntityId cannot be empty.");
        }

        var cleanDescription = Sanitize(description);
        var formattedMessage = $"[Audit] Op: {operationName.Trim()} | Entity: {entityType.Trim()} ({entityId.Trim()}) | Desc: {cleanDescription}";

        return new SystemLog(level, "Audit", formattedMessage);
    }

    private static string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        // 1. Redact key-value pairs like key: value or key=value to hide the actual credential values
        var keyValuePattern = @"(secret_key|api_key|apikey|secret|password)(\s*[:=]\s*)([^\s,|]+)";
        var sanitized = System.Text.RegularExpressions.Regex.Replace(
            input,
            keyValuePattern,
            "$1$2[REDACTED]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        // 2. Also redact any remaining standalone sensitive words to be absolutely secure
        var sensitivePatterns = new[] { "secret_key", "api_key", "apikey", "secret", "password" };
        foreach (var pattern in sensitivePatterns)
        {
            sanitized = System.Text.RegularExpressions.Regex.Replace(
                sanitized,
                @"\b" + pattern + @"\b",
                "[REDACTED]",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        return sanitized;
    }
}
