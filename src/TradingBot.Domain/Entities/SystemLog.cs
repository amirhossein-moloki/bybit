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
}
