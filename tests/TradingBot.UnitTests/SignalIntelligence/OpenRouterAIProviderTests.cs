using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using TradingBot.Parser.Configuration;
using TradingBot.Parser.Services;
using Xunit;

namespace TradingBot.UnitTests.SignalIntelligence;

public class OpenRouterAIProviderTests
{
    private readonly Mock<ILogger<OpenRouterAIProvider>> _mockLogger = new();

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();
        public int CallCount { get; private set; }

        public void EnqueueResponse(HttpResponseMessage response)
        {
            _responses.Enqueue(response);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (_responses.TryDequeue(out var response))
            {
                return Task.FromResult(response);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldReturnResponse_WhenApiCallIsSuccessful()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler();
        var mockResponseJson = "{\"choices\": [{\"message\": {\"role\": \"assistant\", \"content\": \"{\\\"type\\\":\\\"SIGNAL\\\",\\\"symbol\\\":\\\"BTCUSDT\\\"}\"}}]}";

        mockHandler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(mockResponseJson, Encoding.UTF8, "application/json")
        });

        var httpClient = new HttpClient(mockHandler);
        var options = Options.Create(new AIOptions
        {
            Provider = "OpenRouter",
            ApiKey = "test_key",
            Model = "openrouter/auto",
            TimeoutSeconds = 10,
            MaxRetries = 0
        });

        var provider = new OpenRouterAIProvider(httpClient, options, _mockLogger.Object);

        // Act
        var result = await provider.AnalyzeAsync("Hello", CancellationToken.None);

        // Assert
        result.Should().Be("{\"type\":\"SIGNAL\",\"symbol\":\"BTCUSDT\"}");
        mockHandler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldThrowHttpRequestException_WhenApiCallReturnsErrorStatusCode_AndMaxRetriesReached()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler();
        mockHandler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Internal Server Error")
        });
        mockHandler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("Internal Server Error")
        });

        var httpClient = new HttpClient(mockHandler);
        var options = Options.Create(new AIOptions
        {
            Provider = "OpenRouter",
            ApiKey = "test_key",
            Model = "openrouter/auto",
            TimeoutSeconds = 10,
            MaxRetries = 1 // 1 retry means 2 attempts total
        });

        var provider = new OpenRouterAIProvider(httpClient, options, _mockLogger.Object);

        // Act
        Func<Task> act = async () => await provider.AnalyzeAsync("Hello", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*Internal Server Error*");
        mockHandler.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldThrowInvalidOperationException_WhenApiReturnsEmptyOrMalformedContent()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler();
        var mockResponseJson = "{\"choices\": []}"; // Empty choices

        mockHandler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(mockResponseJson, Encoding.UTF8, "application/json")
        });

        var httpClient = new HttpClient(mockHandler);
        var options = Options.Create(new AIOptions
        {
            Provider = "OpenRouter",
            ApiKey = "test_key",
            Model = "openrouter/auto",
            TimeoutSeconds = 10,
            MaxRetries = 0
        });

        var provider = new OpenRouterAIProvider(httpClient, options, _mockLogger.Object);

        // Act
        Func<Task> act = async () => await provider.AnalyzeAsync("Hello", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*empty or malformed*");
    }

    [Fact]
    public async Task AnalyzeAsync_ShouldRetryOnTransientErrors_AndSucceed_WhenSubsequentAttemptsSucceed()
    {
        // Arrange
        var mockHandler = new MockHttpMessageHandler();

        // First attempt fails with transient error
        mockHandler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("Service Unavailable")
        });

        // Second attempt succeeds
        var mockResponseJson = "{\"choices\": [{\"message\": {\"role\": \"assistant\", \"content\": \"Success\"}}]}";
        mockHandler.EnqueueResponse(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(mockResponseJson, Encoding.UTF8, "application/json")
        });

        var httpClient = new HttpClient(mockHandler);
        var options = Options.Create(new AIOptions
        {
            Provider = "OpenRouter",
            ApiKey = "test_key",
            Model = "openrouter/auto",
            TimeoutSeconds = 10,
            MaxRetries = 2
        });

        var provider = new OpenRouterAIProvider(httpClient, options, _mockLogger.Object);

        // Act
        var result = await provider.AnalyzeAsync("Hello", CancellationToken.None);

        // Assert
        result.Should().Be("Success");
        mockHandler.CallCount.Should().Be(2);
    }
}
