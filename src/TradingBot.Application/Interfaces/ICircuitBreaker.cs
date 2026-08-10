using System;
using TradingBot.Application.Enums;

namespace TradingBot.Application.Interfaces;

public interface ICircuitBreaker
{
    string Name { get; }
    CircuitState State { get; }
    bool IsAllowed();
    void RecordSuccess();
    void RecordFailure(Exception exception);
    void Reset();
}
