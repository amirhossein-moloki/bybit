using System;
using System.Linq;
using TradingBot.Domain.Exceptions;

namespace TradingBot.Domain.SignalIntelligence.Entities;

public class MessageProcessingTracker
{
    public Guid Id { get; private set; }
    public Guid TelegramMessageId { get; private set; }
    public string State { get; private set; } // RECEIVED, PROCESSING, ANALYZED, VALIDATED, PUBLISHED, FAILED
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // Required for EF Core
    private MessageProcessingTracker()
    {
        Id = Guid.Empty;
        TelegramMessageId = Guid.Empty;
        State = "RECEIVED";
        CreatedAt = DateTime.UtcNow;
    }

    public MessageProcessingTracker(Guid telegramMessageId, string state)
    {
        if (telegramMessageId == Guid.Empty)
        {
            throw new DomainException("TelegramMessageId is required.");
        }

        Id = Guid.NewGuid();
        TelegramMessageId = telegramMessageId;
        State = ValidateState(state);
        CreatedAt = DateTime.UtcNow;
    }

    public void TransitionTo(string newState)
    {
        string validatedNewState = ValidateState(newState);

        // Transition rules:
        // PUBLISHED is terminal
        if (State == "PUBLISHED" && validatedNewState != "PUBLISHED")
        {
            throw new DomainException($"Cannot transition from terminal state {State} to {validatedNewState}.");
        }

        // FAILED is terminal or can transition back to PROCESSING during retry
        if (State == "FAILED" && validatedNewState != "FAILED" && validatedNewState != "PROCESSING")
        {
            throw new DomainException($"Invalid transition from {State} to {validatedNewState}. Only 'PROCESSING' or 'FAILED' is allowed.");
        }

        // RECEIVED can transition to PROCESSING or FAILED
        if (State == "RECEIVED" && validatedNewState != "PROCESSING" && validatedNewState != "FAILED" && validatedNewState != "RECEIVED")
        {
            throw new DomainException($"Invalid transition from {State} to {validatedNewState}.");
        }

        // PROCESSING can transition to ANALYZED or FAILED
        if (State == "PROCESSING" && validatedNewState != "ANALYZED" && validatedNewState != "FAILED" && validatedNewState != "PROCESSING")
        {
            throw new DomainException($"Invalid transition from {State} to {validatedNewState}.");
        }

        // ANALYZED can transition to VALIDATED or FAILED
        if (State == "ANALYZED" && validatedNewState != "VALIDATED" && validatedNewState != "FAILED" && validatedNewState != "ANALYZED")
        {
            throw new DomainException($"Invalid transition from {State} to {validatedNewState}.");
        }

        // VALIDATED can transition to PUBLISHED or FAILED
        if (State == "VALIDATED" && validatedNewState != "PUBLISHED" && validatedNewState != "FAILED" && validatedNewState != "VALIDATED")
        {
            throw new DomainException($"Invalid transition from {State} to {validatedNewState}.");
        }

        State = validatedNewState;
        UpdatedAt = DateTime.UtcNow;
    }

    private string ValidateState(string state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            throw new DomainException("State cannot be null or empty.");
        }

        var upper = state.ToUpperInvariant();
        var allowed = new[] { "RECEIVED", "PROCESSING", "ANALYZED", "VALIDATED", "PUBLISHED", "FAILED" };
        if (!allowed.Contains(upper))
        {
            throw new DomainException($"Invalid MessageProcessingState: {state}");
        }
        return upper;
    }
}
