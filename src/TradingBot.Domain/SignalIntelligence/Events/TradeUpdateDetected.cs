using System;

namespace TradingBot.Domain.SignalIntelligence.Events;

public record TradeUpdateDetected(
    Guid EventId,
    DateTime Timestamp,
    string CorrelationId,
    string Source,
    string Payload
) : IntelligenceEventBase(EventId, Timestamp, CorrelationId, Source, Payload);
