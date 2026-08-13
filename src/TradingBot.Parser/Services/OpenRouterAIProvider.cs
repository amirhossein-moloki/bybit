using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Parser.Configuration;

namespace TradingBot.Parser.Services;

public class OpenRouterAIProvider : IAIProvider
{
    private readonly HttpClient _httpClient;
    private readonly AIOptions _options;
    private readonly ILogger<OpenRouterAIProvider> _logger;

    public OpenRouterAIProvider(
        HttpClient httpClient,
        IOptions<AIOptions> options,
        ILogger<OpenRouterAIProvider> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> AnalyzeAsync(string prompt, CancellationToken token)
    {
        _logger.LogInformation("OpenRouter AI Provider requested. Model: {Model}.", _options.Model);

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
                _logger.LogWarning(ex, "OpenRouter AI Provider transient error. Retrying {Retry}/{Max}.", retries, maxRetries);
                await Task.Delay(100 * retries, token);
            }
        }
    }

    private async Task<string> ExecuteAnalyzeWithTimeoutAsync(string prompt, CancellationToken token)
    {
        var endpoint = string.IsNullOrWhiteSpace(_options.Endpoint)
            ? "https://openrouter.ai/api/v1/chat/completions"
            : _options.Endpoint;

        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint);

        // Headers
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }

        requestMessage.Headers.Add("HTTP-Referer", "https://github.com/Amir/TradingBot");
        requestMessage.Headers.Add("X-Title", "TradingBot");

        // Payload
        var requestPayload = new OpenRouterRequest
        {
            Model = string.IsNullOrWhiteSpace(_options.Model) ? "openrouter/auto" : _options.Model,
            Messages = new()
            {
                new OpenRouterMessage { Role = "user", Content = prompt }
            }
        };

        var json = JsonSerializer.Serialize(requestPayload);
        requestMessage.Content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(requestMessage, token);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(token);
            _logger.LogError("OpenRouter API error. Status: {StatusCode}, Error: {Error}", response.StatusCode, errorContent);
            throw new HttpRequestException($"OpenRouter API call failed with status {response.StatusCode}: {errorContent}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(token);
        var apiResponse = JsonSerializer.Deserialize<OpenRouterResponse>(responseJson);

        if (apiResponse?.Choices == null || apiResponse.Choices.Count == 0)
        {
            throw new InvalidOperationException("OpenRouter API returned an empty or malformed completion content.");
        }

        var content = apiResponse.Choices[0]?.Message?.Content;
        if (string.IsNullOrEmpty(content))
        {
            throw new InvalidOperationException("OpenRouter API returned an empty or malformed completion content.");
        }

        return content;
    }

    private class OpenRouterRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<OpenRouterMessage> Messages { get; set; } = new();
    }

    private class OpenRouterMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }

    private class OpenRouterResponse
    {
        [JsonPropertyName("choices")]
        public List<OpenRouterChoice>? Choices { get; set; }
    }

    private class OpenRouterChoice
    {
        [JsonPropertyName("message")]
        public OpenRouterResponseMessage? Message { get; set; }
    }

    private class OpenRouterResponseMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }
}
