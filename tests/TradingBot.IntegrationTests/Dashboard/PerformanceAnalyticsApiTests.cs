using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.Application.Analytics.DTOs;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.ValueObjects;
using TradingBot.Persistence.Context;
using Xunit;

using SymbolValueObject = TradingBot.Domain.ValueObjects.Symbol;

namespace TradingBot.IntegrationTests.Dashboard;

public class PerformanceAnalyticsApiTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private HttpClient _client = null!;

    public PerformanceAnalyticsApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TradingDbContext>();

        await context.Database.EnsureCreatedAsync();

        // Clear existing records to ensure deterministic tests
        context.Orders.RemoveRange(context.Orders);
        context.Positions.RemoveRange(context.Positions);
        context.Trades.RemoveRange(context.Trades);
        await context.SaveChangesAsync();

        // Seed data for analytics
        var baseTime = DateTime.UtcNow;

        var o1 = new Order("AN-O1", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1m), new Money(50000m));
        o1.UpdateStatus(OrderStatus.Filled);
        var o2 = new Order("AN-O2", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1m), new Money(50000m));
        o2.UpdateStatus(OrderStatus.Filled);
        context.Orders.AddRange(o1, o2);
        await context.SaveChangesAsync();

        var pos1 = new Position(o1.Id, "BTCUSDT", OrderSide.Buy, 50000m, 1m, margin: 100m, initialStatus: PositionStatus.Closed);
        var pos2 = new Position(o2.Id, "BTCUSDT", OrderSide.Buy, 50000m, 1m, margin: 100m, initialStatus: PositionStatus.Closed);
        context.Positions.AddRange(pos1, pos2);
        await context.SaveChangesAsync();

        var t1 = new Trade(pos1.Id, 50000m, 51000m, 1m, 1000m, 10m, baseTime.AddMinutes(-10));
        SetFieldOrProperty(t1, "OpenedAt", baseTime.AddMinutes(-40));
        SetFieldOrProperty(t1, "ClosedAt", baseTime.AddMinutes(-10));
        SetFieldOrProperty(t1, "NetPnL", 990m); // Win

        var t2 = new Trade(pos2.Id, 50000m, 49500m, 1m, -500m, 10m, baseTime);
        SetFieldOrProperty(t2, "OpenedAt", baseTime.AddMinutes(-20));
        SetFieldOrProperty(t2, "ClosedAt", baseTime);
        SetFieldOrProperty(t2, "NetPnL", -510m); // Loss

        context.Trades.AddRange(t1, t2);
        await context.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    private static void SetFieldOrProperty(object obj, string name, object? value)
    {
        var type = obj.GetType();
        var field = type.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        if (field != null)
        {
            field.SetValue(obj, value);
            return;
        }

        var prop = type.GetProperty(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        if (prop != null && prop.CanWrite)
        {
            prop.SetValue(obj, value);
        }
    }

    private void SetToken(string token)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private void ClearToken()
    {
        _client.DefaultRequestHeaders.Authorization = null;
    }

    [Fact]
    public async Task AnalyticsEndpoints_Unauthenticated_ShouldReturn401()
    {
        ClearToken();

        var endpoints = new[]
        {
            "/api/analytics/performance",
            "/api/analytics/drawdown",
            "/api/analytics/streaks",
            "/api/analytics/duration",
            "/api/analytics/side-performance"
        };

        foreach (var endpoint in endpoints)
        {
            var response = await _client.GetAsync(endpoint);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
    }

    [Fact]
    public async Task GetPerformance_Authenticated_ShouldReturnCorrectData()
    {
        SetToken("ValidDashboardReadToken");

        var response = await _client.GetAsync("/api/analytics/performance");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<Envelope<PerformanceMetricsDto>>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("success");
        result.Data.Should().NotBeNull();
        result.Data.TotalTrades.Should().Be(2);
        result.Data.WinningTrades.Should().Be(1);
        result.Data.LosingTrades.Should().Be(1);
        result.Data.NetPnL.Should().Be(480m);
    }

    [Fact]
    public async Task GetDrawdown_Authenticated_ShouldReturnCorrectData()
    {
        SetToken("ValidDashboardReadToken");

        var response = await _client.GetAsync("/api/analytics/drawdown?initialBalance=10000");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<Envelope<DrawdownMetricsDto>>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("success");
        result.Data.Should().NotBeNull();
        result.Data.PeakEquity.Should().Be(10990m);
        result.Data.CurrentEquity.Should().Be(10480m);
        result.Data.MaximumDrawdown.Should().Be(510m);
    }

    [Fact]
    public async Task GetStreaks_Authenticated_ShouldReturnCorrectData()
    {
        SetToken("ValidDashboardReadToken");

        var response = await _client.GetAsync("/api/analytics/streaks");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<Envelope<StreakMetricsDto>>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("success");
        result.Data.Should().NotBeNull();
        result.Data.MaximumWinStreak.Should().Be(1);
        result.Data.MaximumLossStreak.Should().Be(1);
    }

    [Fact]
    public async Task GetDuration_Authenticated_ShouldReturnCorrectData()
    {
        SetToken("ValidDashboardReadToken");

        var response = await _client.GetAsync("/api/analytics/duration");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<Envelope<DurationMetricsDto>>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("success");
        result.Data.Should().NotBeNull();
        result.Data.AverageDuration.Should().Be(TimeSpan.FromMinutes(25));
    }

    [Fact]
    public async Task GetSidePerformance_Authenticated_ShouldReturnCorrectData()
    {
        SetToken("ValidDashboardReadToken");

        var response = await _client.GetAsync("/api/analytics/side-performance");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<Envelope<LongShortPerformanceDto>>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("success");
        result.Data.Should().NotBeNull();
        result.Data.Long.Trades.Should().Be(2);
        result.Data.Long.TotalPnL.Should().Be(480m);
        result.Data.Short.Trades.Should().Be(0);
    }

    [Theory]
    [InlineData("/api/analytics/performance?startDate=invalid-date")]
    [InlineData("/api/analytics/performance?startDate=2026-08-10&endDate=2026-08-01")]
    [InlineData("/api/analytics/drawdown?initialBalance=-100")]
    public async Task InvalidParameters_ShouldReturnBadRequest(string url)
    {
        SetToken("ValidDashboardReadToken");

        var response = await _client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var result = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("error");
        result.Error.Code.Should().Be("VALIDATION_FAILED");
    }

    public class Envelope<T>
    {
        public string Status { get; set; } = null!;
        public T Data { get; set; } = default!;
    }

    public class ErrorEnvelope
    {
        public string Status { get; set; } = null!;
        public ErrorDetail Error { get; set; } = null!;
    }

    public class ErrorDetail
    {
        public string Code { get; set; } = null!;
        public string Message { get; set; } = null!;
    }
}
