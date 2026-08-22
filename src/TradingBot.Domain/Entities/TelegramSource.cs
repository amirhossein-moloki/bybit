using System;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.Entities;

public class TelegramSource
{
    public Guid Id { get; private set; }
    public long TelegramChatId { get; private set; }
    public string Title { get; private set; }
    public string? Username { get; private set; }
    public TelegramSourceType Type { get; private set; }
    public bool IsEnabled { get; private set; }
    public bool ListenForSignals { get; private set; }
    public bool ProcessMessages { get; private set; }
    public DateTime? PausedUntil { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // EF Core constructor
    private TelegramSource()
    {
        Id = Guid.Empty;
        Title = string.Empty;
        CreatedAt = DateTime.UtcNow;
    }

    public TelegramSource(
        long telegramChatId,
        string title,
        string? username = null,
        TelegramSourceType type = TelegramSourceType.Channel,
        bool isEnabled = true,
        bool listenForSignals = true,
        bool processMessages = true)
    {
        if (telegramChatId == 0)
        {
            throw new DomainException("TelegramChatId cannot be 0.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Title cannot be empty.");
        }

        Id = Guid.NewGuid();
        TelegramChatId = telegramChatId;
        Title = title.Trim();
        Username = NormalizeUsername(username);
        Type = type;
        IsEnabled = isEnabled;
        ListenForSignals = listenForSignals;
        ProcessMessages = processMessages;
        PausedUntil = null;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateMetadata(string title, string? username, TelegramSourceType type)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DomainException("Title cannot be empty.");
        }

        Title = title.Trim();
        Username = NormalizeUsername(username);
        Type = type;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateCapabilities(bool isEnabled, bool listenForSignals, bool processMessages)
    {
        IsEnabled = isEnabled;
        ListenForSignals = listenForSignals;
        ProcessMessages = processMessages;

        if (isEnabled && IsPaused)
        {
            // Resume if explicitly enabling
            PausedUntil = null;
        }

        UpdatedAt = DateTime.UtcNow;
    }

    public void SetEnabled(bool enabled)
    {
        IsEnabled = enabled;
        if (enabled)
        {
            PausedUntil = null;
        }
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetListenForSignals(bool listen)
    {
        ListenForSignals = listen;
        UpdatedAt = DateTime.UtcNow;
    }

    public void SetProcessMessages(bool process)
    {
        ProcessMessages = process;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Pause(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new DomainException("Pause duration must be greater than zero.");
        }

        PausedUntil = DateTime.UtcNow.Add(duration);
        UpdatedAt = DateTime.UtcNow;
    }

    public void Resume()
    {
        PausedUntil = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public bool IsActive => IsEnabled && (!PausedUntil.HasValue || PausedUntil.Value <= DateTime.UtcNow);

    public bool IsPaused => IsEnabled && PausedUntil.HasValue && PausedUntil.Value > DateTime.UtcNow;

    private static string? NormalizeUsername(string? username)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;
        var trimmed = username.Trim();
        if (!trimmed.StartsWith("@") && !trimmed.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return "@" + trimmed;
        }
        return trimmed;
    }
}
