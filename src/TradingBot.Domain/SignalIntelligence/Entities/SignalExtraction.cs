using System;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.SignalIntelligence.Entities;

public class SignalExtraction
{
    public Guid Id { get; private set; }
    public Guid TelegramMessageId { get; private set; }
    public long MessageId { get; private set; }
    public string? Symbol { get; private set; }
    public string Side { get; private set; }
    public decimal? EntryPrice { get; private set; }
    public decimal? StopLoss { get; private set; }
    public string TakeProfitData { get; private set; }
    public decimal Confidence { get; private set; }
    public string Status { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Required for EF Core
    private SignalExtraction()
    {
        Id = Guid.Empty;
        TelegramMessageId = Guid.Empty;
        Side = "UNKNOWN";
        TakeProfitData = "[]";
        Status = "Invalid";
        CreatedAt = DateTime.UtcNow;
    }

    public SignalExtraction(
        Guid telegramMessageId,
        long messageId,
        string? symbol,
        string side,
        decimal? entryPrice,
        decimal? stopLoss,
        string takeProfitData,
        decimal confidence,
        string status)
    {
        if (telegramMessageId == Guid.Empty)
        {
            throw new DomainException("TelegramMessageId is required.");
        }

        if (confidence < 0m || confidence > 1m)
        {
            throw new DomainException("Confidence must be between 0 and 1.");
        }

        Id = Guid.NewGuid();
        TelegramMessageId = telegramMessageId;
        MessageId = messageId;
        Symbol = symbol;
        Side = side ?? "UNKNOWN";
        EntryPrice = entryPrice;
        StopLoss = stopLoss;
        TakeProfitData = takeProfitData ?? "[]";
        Confidence = confidence;
        Status = status ?? "Invalid";
        CreatedAt = DateTime.UtcNow;
    }
}
