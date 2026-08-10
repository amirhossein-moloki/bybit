using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Configuration;
using TradingBot.Application.Interfaces;

namespace TradingBot.Application.Services;

public class CircuitBreakerRegistry : ICircuitBreakerRegistry
{
    private readonly ConcurrentDictionary<string, ICircuitBreaker> _breakers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ReliabilityOptions _options;
    private readonly IErrorClassifier _errorClassifier;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerFactory _loggerFactory;

    public CircuitBreakerRegistry(
        ReliabilityOptions options,
        IErrorClassifier errorClassifier,
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _errorClassifier = errorClassifier ?? throw new ArgumentNullException(nameof(errorClassifier));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    public ICircuitBreaker GetOrCreate(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) name = "Default";

        return _breakers.GetOrAdd(name, key =>
        {
            var logger = _loggerFactory.CreateLogger<CircuitBreaker>();
            return new CircuitBreaker(key, _options, _errorClassifier, _serviceProvider, logger);
        });
    }

    public IReadOnlyDictionary<string, ICircuitBreaker> GetAll()
    {
        return _breakers;
    }
}
