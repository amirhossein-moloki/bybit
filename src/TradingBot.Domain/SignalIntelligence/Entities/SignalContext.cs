using System;
using TradingBot.Domain.Exceptions;
using TradingBot.Domain.SignalIntelligence.Enums;

namespace TradingBot.Domain.SignalIntelligence.Entities;

public class SignalContext
{
    public Guid Id { get; private set; }
    public Guid SignalId { get; private set; }
    public long ChannelId { get; private set; }
    public string Symbol { get; private set; }
    public SignalState CurrentState { get; private set; }
    public string? LastAction { get; private set; }
    public long LastMessageId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // Required for EF Core
    private SignalContext()
    {
        Id = Guid.Empty;
        SignalId = Guid.Empty;
        Symbol = string.Empty;
        CurrentState = SignalState.RECEIVED;
        CreatedAt = DateTime.UtcNow;
    }

    public SignalContext(
        Guid signalId,
        long channelId,
        string symbol,
        SignalState currentState,
        string? lastAction,
        long lastMessageId)
    {
        if (signalId == Guid.Empty)
        {
            throw new DomainException("SignalId is required.");
        }

        if (channelId == 0)
        {
            throw new DomainException("ChannelId is required.");
        }

        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new DomainException("Symbol is required.");
        }

        if (lastMessageId <= 0)
        {
            throw new DomainException("LastMessageId must be greater than zero.");
        }

        if (!Enum.IsDefined(typeof(SignalState), currentState))
        {
            throw new DomainException("CurrentState is invalid.");
        }

        Id = Guid.NewGuid();
        SignalId = signalId;
        ChannelId = channelId;
        Symbol = symbol.ToUpperInvariant();
        CurrentState = currentState;
        LastAction = lastAction;
        LastMessageId = lastMessageId;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = null;
    }

    public void UpdateState(SignalState newState, string? lastAction, long lastMessageId)
    {
        if (!Enum.IsDefined(typeof(SignalState), newState))
        {
            throw new DomainException("NewState is invalid.");
        }

        if (lastMessageId <= 0)
        {
            throw new DomainException("LastMessageId must be greater than zero.");
        }

        CurrentState = newState;
        LastAction = lastAction;
        LastMessageId = lastMessageId;
        UpdatedAt = DateTime.UtcNow;
    }
}
