using System;

namespace TradingBot.Domain.SignalIntelligence.Interfaces;

public interface IIntelligenceEvent
{
    Guid EventId { get; }
    DateTime Timestamp { get; }
    string CorrelationId { get; }
    string Source { get; }
    string Payload { get; }
}
