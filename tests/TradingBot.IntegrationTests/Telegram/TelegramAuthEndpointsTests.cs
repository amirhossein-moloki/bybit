using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using TradingBot.Telegram.Interfaces;
using TradingBot.Telegram.Models;
using Xunit;

namespace TradingBot.IntegrationTests.Telegram;

public class TelegramAuthEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public TelegramAuthEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "ValidDashboardReadToken");
    }

    [Fact]
    public async Task GetStatus_ShouldReturn200AndStatusDto()
    {
        // Act
        var response = await _client.GetAsync("/api/telegram/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.GetProperty("status").GetString().Should().Be("success");
        json.GetProperty("data").GetProperty("status").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task StartQrAuth_ShouldReturn200AndQrStartResult()
    {
        // Act
        var response = await _client.PostAsync("/api/telegram/auth/qr/start", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();

        json.GetProperty("status").GetString().Should().Be("success");
        var data = json.GetProperty("data");
        data.GetProperty("sessionId").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetQrStatus_ShouldReturn200AndQrStatusDto()
    {
        // Act
        var startResponse = await _client.PostAsync("/api/telegram/auth/qr/start", null);
        startResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var startJson = await startResponse.Content.ReadFromJsonAsync<JsonElement>();
        var sessionId = startJson.GetProperty("data").GetProperty("sessionId").GetString();

        var statusResponse = await _client.GetAsync($"/api/telegram/auth/qr/status?sessionId={sessionId}");

        // Assert
        statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var statusJson = await statusResponse.Content.ReadFromJsonAsync<JsonElement>();
        statusJson.GetProperty("status").GetString().Should().Be("success");
    }

    [Fact]
    public async Task StartOtpLogin_ShouldReturn400_WhenPhoneNumberIsEmpty()
    {
        // Arrange
        var request = new OtpStartRequest { PhoneNumber = "" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/telegram/auth/otp/start", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("success").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task VerifyOtp_ShouldReturn400_WhenCodeIsEmpty()
    {
        // Arrange
        var request = new OtpVerifyRequest { PhoneNumber = "+1234567890", PhoneCodeHash = "hash", Code = "" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/telegram/auth/otp/verify", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("success").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task VerifyPassword_ShouldReturn400_WhenPasswordIsEmpty()
    {
        // Arrange
        var request = new PasswordVerifyRequest { Password = "" };

        // Act
        var response = await _client.PostAsJsonAsync("/api/telegram/auth/password", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("success").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Logout_ShouldReturn200SuccessMessage()
    {
        // Act
        var response = await _client.PostAsync("/api/telegram/auth/logout", null);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("status").GetString().Should().Be("success");
    }

    [Fact]
    public async Task Endpoints_ShouldReturn401_WhenAuthorizationHeaderIsMissing()
    {
        // Arrange
        using var unauthClient = _factory.CreateClient();

        // Act
        var response = await unauthClient.GetAsync("/api/telegram/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
