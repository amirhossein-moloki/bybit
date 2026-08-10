using System.Collections.Generic;

namespace TradingBot.Application.Interfaces;

public interface ICircuitBreakerRegistry
{
    ICircuitBreaker GetOrCreate(string name);
    IReadOnlyDictionary<string, ICircuitBreaker> GetAll();
}
