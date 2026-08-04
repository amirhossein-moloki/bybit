using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TradingBot.Application.Interfaces;
using TradingBot.Worker;
using Xunit;

namespace TradingBot.IntegrationTests;

public class HealthCheckTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthCheckTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HealthEndpoint_ShouldReturnHealthyAndStatus200_WhenAppIsRunning()
    {
        // Arrange
        var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                var mockExchangeClient = new Mock<IExchangeClient>();
                mockExchangeClient.Setup(x => x.ExchangeName).Returns("Bybit");
                mockExchangeClient.Setup(x => x.PingAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

                // Register mock to override the real ExchangeClient in testing
                services.AddSingleton(mockExchangeClient.Object);
            });
        }).CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Be("Healthy");
    }

    [Fact]
    public async Task RootEndpoint_ShouldReturnJsonWithStatusOnline_WhenAppIsRunning()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("\"status\":\"Online\"");
    }
}
