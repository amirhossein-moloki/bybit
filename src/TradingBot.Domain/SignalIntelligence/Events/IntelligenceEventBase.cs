using System;
using TradingBot.Domain.SignalIntelligence.Interfaces;

namespace TradingBot.Domain.SignalIntelligence.Events;

public abstract record IntelligenceEventBase(
    Guid EventId,
    DateTime Timestamp,
    string CorrelationId,
    string Source,
    string Payload
) : IIntelligenceEvent;
