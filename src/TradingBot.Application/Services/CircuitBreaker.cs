using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Configuration;
using TradingBot.Application.Enums;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Monitoring;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Services;

public class CircuitBreaker : ICircuitBreaker
{
    private readonly string _name;
    private readonly ReliabilityOptions _options;
    private readonly IErrorClassifier _errorClassifier;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CircuitBreaker> _logger;
    private readonly object _lock = new();

    private CircuitState _state = CircuitState.Closed;
    private int _failureCount = 0;
    private int _activeProbesCount = 0;
    private DateTime _nextAttemptTime = DateTime.MinValue;

    public string Name => _name;

    public CircuitState State
    {
        get
        {
            lock (_lock) return _state;
        }
    }

    public CircuitBreaker(
        string name,
        ReliabilityOptions options,
        IErrorClassifier errorClassifier,
        IServiceProvider serviceProvider,
        ILogger<CircuitBreaker> logger)
    {
        _name = name ?? throw new ArgumentNullException(nameof(name));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _errorClassifier = errorClassifier ?? throw new ArgumentNullException(nameof(errorClassifier));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsAllowed()
    {
        if (!_options.CircuitBreaker.Enabled) return true;

        lock (_lock)
        {
            if (_state == CircuitState.Closed)
            {
                return true;
            }

            if (_state == CircuitState.Open)
            {
                if (DateTime.UtcNow >= _nextAttemptTime)
                {
                    _state = CircuitState.HalfOpen;
                    _activeProbesCount = 1;
                    _logger.LogWarning("CircuitBreaker '{Name}' transitioned to HALF-OPEN. Probing connection...", _name);
                    _ = PublishEventAsync("CircuitHalfOpened", "WARNING", "HalfOpen", $"Circuit breaker '{_name}' transitioned to HALF-OPEN.");
                    return true;
                }
                return false;
            }

            if (_state == CircuitState.HalfOpen)
            {
                if (_activeProbesCount < _options.CircuitBreaker.HalfOpenProbeCount)
                {
                    _activeProbesCount++;
                    return true;
                }
                return false;
            }

            return false;
        }
    }

    public void RecordSuccess()
    {
        if (!_options.CircuitBreaker.Enabled) return;

        lock (_lock)
        {
            if (_state == CircuitState.HalfOpen)
            {
                _state = CircuitState.Closed;
                _failureCount = 0;
                _activeProbesCount = 0;
                _logger.LogInformation("CircuitBreaker '{Name}' transitioned to CLOSED. Dependency is healthy.", _name);
                _ = PublishEventAsync("CircuitClosed", "INFORMATION", "Closed", $"Circuit breaker '{_name}' transitioned to CLOSED.");
            }
            else if (_state == CircuitState.Closed)
            {
                _failureCount = 0;
            }
        }
    }

    public void RecordFailure(Exception exception)
    {
        if (!_options.CircuitBreaker.Enabled) return;
        if (exception == null) return;

        // Check error classification: only count instability failures
        var retryability = _errorClassifier.Classify(exception);
        if (retryability != ErrorRetryability.Retryable)
        {
            _logger.LogDebug("CircuitBreaker '{Name}': Ignored non-transient error for failure counting. Error: {Error}", _name, exception.Message);
            return;
        }

        lock (_lock)
        {
            _failureCount++;
            _logger.LogWarning("CircuitBreaker '{Name}': Recorded failure. Current failures: {Count}/{Threshold}",
                _name, _failureCount, _options.CircuitBreaker.FailureThreshold);

            if (_state == CircuitState.Closed)
            {
                if (_failureCount >= _options.CircuitBreaker.FailureThreshold)
                {
                    _state = CircuitState.Open;
                    _nextAttemptTime = DateTime.UtcNow.Add(_options.CircuitBreaker.BreakDuration);
                    _logger.LogError("CircuitBreaker '{Name}' transitioned to OPEN due to {Failures} consecutive failures. Break duration: {Duration}s.",
                        _name, _failureCount, _options.CircuitBreaker.BreakDuration.TotalSeconds);
                    _ = PublishEventAsync("CircuitOpened", "CRITICAL", "Open", $"Circuit breaker '{_name}' transitioned to OPEN.");
                }
            }
            else if (_state == CircuitState.HalfOpen)
            {
                _state = CircuitState.Open;
                _nextAttemptTime = DateTime.UtcNow.Add(_options.CircuitBreaker.BreakDuration);
                _logger.LogError("CircuitBreaker '{Name}' transitioned back to OPEN after probe failed. Break duration: {Duration}s. Exception: {Message}",
                    _name, _options.CircuitBreaker.BreakDuration.TotalSeconds, exception.Message);
                _ = PublishEventAsync("CircuitOpened", "CRITICAL", "Open", $"Circuit breaker '{_name}' transitioned to OPEN.");
            }
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            var oldState = _state;
            _state = CircuitState.Closed;
            _failureCount = 0;
            _activeProbesCount = 0;
            _nextAttemptTime = DateTime.MinValue;

            if (oldState != CircuitState.Closed)
            {
                _logger.LogInformation("CircuitBreaker '{Name}' was reset manually to CLOSED.", _name);
                _ = PublishEventAsync("CircuitClosed", "INFORMATION", "Closed", $"Circuit breaker '{_name}' was reset manually to CLOSED.");
            }
        }
    }

    private async Task PublishEventAsync(string eventType, string severity, string status, string message)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var publisher = scope.ServiceProvider.GetService<IMonitoringEventPublisher>();
            if (publisher != null)
            {
                var @event = new MonitoringEvent(
                    eventType: eventType,
                    severity: severity,
                    source: "System",
                    component: _name,
                    status: status,
                    message: message
                );
                await publisher.PublishAsync(@event, forceSynchronous: false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CircuitBreaker '{Name}': Failed to publish monitoring event.", _name);
        }
    }
}
