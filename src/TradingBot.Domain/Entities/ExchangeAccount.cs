using System;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.Entities;

public class ExchangeAccount
{
    public Guid Id { get; private set; }
    public string ExchangeName { get; private set; }
    public string Environment { get; private set; }
    public string EncryptedApiKey { get; private set; }
    public string EncryptedSecret { get; private set; }
    public ExchangeAccountStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // Required for EF Core
    private ExchangeAccount()
    {
        Id = Guid.Empty;
        ExchangeName = string.Empty;
        Environment = string.Empty;
        EncryptedApiKey = string.Empty;
        EncryptedSecret = string.Empty;
        Status = ExchangeAccountStatus.Inactive;
        CreatedAt = DateTime.UtcNow;
    }

    public ExchangeAccount(string exchangeName, string environment, string encryptedApiKey, string encryptedSecret)
    {
        if (string.IsNullOrWhiteSpace(exchangeName))
        {
            throw new DomainException("ExchangeName cannot be null or empty.");
        }

        if (string.IsNullOrWhiteSpace(environment))
        {
            throw new DomainException("Environment cannot be null or empty.");
        }

        if (string.IsNullOrWhiteSpace(encryptedApiKey))
        {
            throw new DomainException("EncryptedApiKey cannot be null or empty.");
        }

        if (string.IsNullOrWhiteSpace(encryptedSecret))
        {
            throw new DomainException("EncryptedSecret cannot be null or empty.");
        }

        Id = Guid.NewGuid();
        ExchangeName = exchangeName.Trim().ToUpperInvariant();
        Environment = environment.Trim().ToLowerInvariant();
        EncryptedApiKey = encryptedApiKey;
        EncryptedSecret = encryptedSecret;
        Status = ExchangeAccountStatus.Active;
        CreatedAt = DateTime.UtcNow;
    }

    public void UpdateCredentials(string encryptedApiKey, string encryptedSecret)
    {
        if (string.IsNullOrWhiteSpace(encryptedApiKey))
        {
            throw new DomainException("EncryptedApiKey cannot be null or empty.");
        }

        if (string.IsNullOrWhiteSpace(encryptedSecret))
        {
            throw new DomainException("EncryptedSecret cannot be null or empty.");
        }

        EncryptedApiKey = encryptedApiKey;
        EncryptedSecret = encryptedSecret;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateStatus(ExchangeAccountStatus newStatus)
    {
        if (Status != newStatus)
        {
            Status = newStatus;
            UpdatedAt = DateTime.UtcNow;
        }
    }
}
