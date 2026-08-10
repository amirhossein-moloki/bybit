using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Domain.Enums;
using TradingBot.Domain.Entities;
using TradingBot.Telegram.Interfaces;
using TradingBot.Telegram.Models;
using TradingBot.Application.Trading.Execution.Contracts;

namespace TradingBot.IntegrationTests.Reliability;

public enum FailureType
{
    None,
    NetworkFailure,
    Timeout,
    ConnectionReset,
    Http5xx,
    RateLimit,
    WebSocketDisconnect,
    DatabaseUnavailable,
    TelegramUnavailable,
    WorkerFailure,
    ApplicationRestart,
    DuplicateEvent,
    UnknownState
}

public class FailureSimulator
{
    private readonly ConcurrentDictionary<string, FailureType> _activeFailures = new();
    private int _remainingFailuresToInject = 0;
    private TimeSpan? _rateLimitDuration;

    public void InjectFailure(string key, FailureType type, int count = 1, TimeSpan? rateLimitDuration = null)
    {
        _activeFailures[key] = type;
        _remainingFailuresToInject = count;
        _rateLimitDuration = rateLimitDuration;
    }

    public void ClearFailure(string key)
    {
        _activeFailures.TryRemove(key, out _);
        _remainingFailuresToInject = 0;
        _rateLimitDuration = null;
    }

    public void ClearAll()
    {
        _activeFailures.Clear();
        _remainingFailuresToInject = 0;
        _rateLimitDuration = null;
    }

    public bool ShouldFail(string key, out FailureType type)
    {
        if (_activeFailures.TryGetValue(key, out type) && type != FailureType.None && _remainingFailuresToInject > 0)
        {
            _remainingFailuresToInject--;
            if (_remainingFailuresToInject == 0)
            {
                _activeFailures.TryRemove(key, out _);
            }
            return true;
        }
        type = FailureType.None;
        return false;
    }

    public void HandleFailureType(FailureType type, string operationName)
    {
        switch (type)
        {
            case FailureType.NetworkFailure:
                throw new HttpRequestException("Network unreachable.", null, HttpStatusCode.ServiceUnavailable);
            case FailureType.Timeout:
                throw new TimeoutException($"Operation '{operationName}' timed out.");
            case FailureType.ConnectionReset:
                throw new HttpRequestException("Connection reset by peer.", new System.IO.IOException("Connection reset"));
            case FailureType.Http5xx:
                throw new HttpRequestException("Internal Server Error", null, HttpStatusCode.InternalServerError);
            case FailureType.RateLimit:
                var duration = _rateLimitDuration ?? TimeSpan.FromSeconds(2);
                throw new SimulatedRateLimitException(duration);
            case FailureType.DatabaseUnavailable:
                throw new InvalidOperationException("The database server was not found or was not accessible.");
            case FailureType.TelegramUnavailable:
                throw new HttpRequestException("Telegram API is currently unavailable (503 Service Unavailable)", null, HttpStatusCode.ServiceUnavailable);
            case FailureType.WorkerFailure:
                throw new InvalidOperationException("Worker thread crashed or aborted.");
            default:
                break;
        }
    }
}

public class SimulatedRateLimitException : Exception
{
    public TimeSpan RetryAfter { get; }

    public SimulatedRateLimitException(TimeSpan retryAfter) : base($"Bybit Error RetCode=33004 RateLimited. Retry after {retryAfter.TotalSeconds} seconds.")
    {
        RetryAfter = retryAfter;
    }
}
