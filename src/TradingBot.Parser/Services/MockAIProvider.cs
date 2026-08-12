using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Parser.Configuration;

namespace TradingBot.Parser.Services;

public class MockAIProvider : IAIProvider
{
    private readonly AIOptions _options;
    private readonly ILogger<MockAIProvider> _logger;
    private static readonly ConcurrentQueue<string> _stubResponses = new();
    private static Func<string, string>? _dynamicResponseFunc;
    private static bool _simulateTimeout = false;
    private static bool _simulateFailure = false;

    public MockAIProvider(IOptions<AIOptions> options, ILogger<MockAIProvider> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static void EnqueueStubResponse(string response)
    {
        _stubResponses.Enqueue(response);
    }

    public static void SetDynamicResponse(Func<string, string> responseFunc)
    {
        _dynamicResponseFunc = responseFunc;
    }

    public static void Clear()
    {
        _stubResponses.Clear();
        _dynamicResponseFunc = null;
        _simulateTimeout = false;
        _simulateFailure = false;
    }

    public static void SimulateTimeout(bool simulate)
    {
        _simulateTimeout = simulate;
    }

    public static void SimulateFailure(bool simulate)
    {
        _simulateFailure = simulate;
    }

    public async Task<string> AnalyzeAsync(string prompt, CancellationToken token)
    {
        _logger.LogInformation("AI Provider requested. Model: {Model}. Provider: {Provider}", _options.Model, _options.Provider);

        if (!string.IsNullOrEmpty(_options.ApiKey))
        {
            _logger.LogDebug("ApiKey exists (not logged for security).");
        }

        int retries = 0;
        int maxRetries = _options.MaxRetries;
        int timeoutMs = _options.TimeoutSeconds * 1000;

        while (true)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(timeoutMs);

                return await ExecuteAnalyzeWithTimeoutAsync(prompt, cts.Token);
            }
            catch (Exception ex) when (retries < maxRetries && !(ex is OperationCanceledException && token.IsCancellationRequested))
            {
                retries++;
                _logger.LogWarning(ex, "AI Provider transient error. Retrying {Retry}/{Max}.", retries, maxRetries);
                await Task.Delay(100 * retries, token);
            }
        }
    }

    private async Task<string> ExecuteAnalyzeWithTimeoutAsync(string prompt, CancellationToken token)
    {
        if (_simulateFailure)
        {
            throw new Exception("Simulated transient AI API error");
        }

        if (_simulateTimeout)
        {
            await Task.Delay(10000, token);
            throw new TimeoutException("Simulated AI API timeout");
        }

        await Task.Delay(10, token);

        if (_dynamicResponseFunc != null)
        {
            return _dynamicResponseFunc(prompt);
        }

        if (_stubResponses.TryDequeue(out var stub))
        {
            return stub;
        }

        return "{\"type\":\"UNKNOWN\",\"confidence\":0.0,\"reason\":\"No mock response registered\"}";
    }
}
