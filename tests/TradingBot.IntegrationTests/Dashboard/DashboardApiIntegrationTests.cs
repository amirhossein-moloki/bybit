using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.Application.Dashboard.DTOs;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.ValueObjects;
using TradingBot.Persistence.Context;
using Xunit;

using SymbolValueObject = TradingBot.Domain.ValueObjects.Symbol;

namespace TradingBot.IntegrationTests.Dashboard;

public class DashboardApiIntegrationTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private HttpClient _client = null!;

    public DashboardApiIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();

        // Seed data in test SQLite Db Context
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TradingDbContext>();

        // Ensure SQLite schema is fully created from current model
        await context.Database.EnsureCreatedAsync();

        // Ensure clean state
        context.Orders.RemoveRange(context.Orders);
        context.Positions.RemoveRange(context.Positions);
        context.Trades.RemoveRange(context.Trades);
        context.Alerts.RemoveRange(context.Alerts);
        context.MonitoringEvents.RemoveRange(context.MonitoringEvents);
        context.HealthCheckResults.RemoveRange(context.HealthCheckResults);
        await context.SaveChangesAsync();

        // Seed Orders
        var activeOrder = new Order("INT-CL-ACTIVE", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.5m), new Money(40000m));
        activeOrder.UpdateStatus(OrderStatus.Pending);

        var filledOrder1 = new Order("INT-CL-FILLED1", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(0.5m), new Money(40000m));
        filledOrder1.UpdateStatus(OrderStatus.Pending);
        filledOrder1.UpdateStatus(OrderStatus.Submitting);
        filledOrder1.UpdateStatus(OrderStatus.Submitted);
        filledOrder1.UpdateStatus(OrderStatus.Filled);

        var filledOrder2 = new Order("INT-CL-FILLED2", new SymbolValueObject("ETHUSDT"), OrderSide.Sell, OrderType.Limit, new Quantity(5m), new Money(2000m));
        filledOrder2.UpdateStatus(OrderStatus.Pending);
        filledOrder2.UpdateStatus(OrderStatus.Submitting);
        filledOrder2.UpdateStatus(OrderStatus.Submitted);
        filledOrder2.UpdateStatus(OrderStatus.Filled);

        context.Orders.AddRange(activeOrder, filledOrder1, filledOrder2);
        await context.SaveChangesAsync();

        // Seed Positions
        var pos1 = new Position(filledOrder1.Id, "BTCUSDT", OrderSide.Buy, 40000m, 0.5m, margin: 200m, initialStatus: PositionStatus.Open);
        pos1.UpdatePrice(41000m);

        var pos2 = new Position(filledOrder2.Id, "ETHUSDT", OrderSide.Sell, 2000m, 5m, margin: 100m, initialStatus: PositionStatus.PartiallyClosed);
        pos2.UpdatePrice(2010m);

        context.Positions.AddRange(pos1, pos2);
        await context.SaveChangesAsync();

        // Seed Trades
        var winTrade = new Trade(pos1.Id, 40000m, 40500m, 0.5m, 250m, 5m, DateTime.UtcNow);
        context.Trades.Add(winTrade);

        // Seed Alerts
        var alert1 = new Alert("R1", "ConnFailed", "ERROR", "Active", "Worker", "CompA", "Error with password=SuperSecretPassword", "D1");
        context.Alerts.Add(alert1);

        // Seed MonitoringEvents
        var ev1 = new MonitoringEvent("EvTypeA", "INFORMATION", "SourceA", "CompA", "StatusA", "My secret token is secret_key=BotTokenValue", timestamp: DateTime.UtcNow.AddSeconds(-10));
        context.MonitoringEvents.Add(ev1);

        // Seed Health Check Results
        var checkDb = new HealthCheckResult("Database", HealthStatus.Healthy, DateTime.UtcNow.AddMinutes(-5), 12);
        context.HealthCheckResults.Add(checkDb);

        await context.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        return Task.CompletedTask;
    }

    private void SetToken(string token)
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private void ClearToken()
    {
        _client.DefaultRequestHeaders.Authorization = null;
    }

    // --- 1. Authentication & Authorization Tests ---

    [Fact]
    public async Task GetOverview_Unauthenticated_ShouldReturn401()
    {
        ClearToken();
        var response = await _client.GetAsync("/api/dashboard/overview");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetOverview_InvalidToken_ShouldReturn401()
    {
        SetToken("FakeTokenString123");
        var response = await _client.GetAsync("/api/dashboard/overview");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetOverview_Authenticated_ButUnauthorized_ShouldReturn403()
    {
        SetToken("ValidDashboardNoReadToken");
        var response = await _client.GetAsync("/api/dashboard/overview");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetOverview_Authenticated_AndAuthorized_ShouldReturn200()
    {
        SetToken("ValidDashboardReadToken");
        var response = await _client.GetAsync("/api/dashboard/overview");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // --- 2. Endpoint Implementation Tests ---

    [Fact]
    public async Task DashboardOverview_ShouldReturnExpectedContractStructure()
    {
        SetToken("ValidDashboardReadToken");
        var response = await _client.GetAsync("/api/dashboard/overview");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<Envelope<DashboardOverviewDto>>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("success");
        result.Data.Should().NotBeNull();
        result.Data.Positions.OpenPositionCount.Should().Be(2);
    }

    [Fact]
    public async Task SystemHealth_ShouldReturnExpectedContractStructure()
    {
        SetToken("ValidDashboardReadToken");
        var response = await _client.GetAsync("/api/dashboard/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<Envelope<SystemHealthOverviewDto>>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("success");
        result.Data.Should().NotBeNull();
        result.Data.Database.Status.Should().Be("Healthy");
    }

    [Fact]
    public async Task TradingOverview_ShouldReturnExpectedContractStructure()
    {
        SetToken("ValidDashboardReadToken");
        var response = await _client.GetAsync("/api/dashboard/trading");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<Envelope<TradingDashboardOverviewDto>>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("success");
        result.Data.Should().NotBeNull();
        result.Data.Orders.TotalOrders.Should().Be(3);
    }

    [Fact]
    public async Task OpenPositions_ShouldReturnExpectedPagedStructure()
    {
        SetToken("ValidDashboardReadToken");
        var response = await _client.GetAsync("/api/dashboard/positions?pageSize=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<Envelope<PagedResultDto<TradingPositionDto>>>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("success");
        result.Data.Should().NotBeNull();
        result.Data.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ActiveOrders_ShouldReturnExpectedPagedStructure()
    {
        SetToken("ValidDashboardReadToken");
        var response = await _client.GetAsync("/api/dashboard/orders?pageSize=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<Envelope<PagedResultDto<TradingOrderDto>>>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("success");
        result.Data.Should().NotBeNull();
        result.Data.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RecentTrades_ShouldReturnExpectedPagedStructure()
    {
        SetToken("ValidDashboardReadToken");
        var response = await _client.GetAsync("/api/dashboard/trades?pageSize=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<Envelope<PagedResultDto<TradingTradeDto>>>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("success");
        result.Data.Should().NotBeNull();
        result.Data.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task TradingPerformance_ShouldReturnExpectedPerformancePayload()
    {
        SetToken("ValidDashboardReadToken");
        var response = await _client.GetAsync("/api/dashboard/performance");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<Envelope<PerformancePayload>>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("success");
        result.Data.Should().NotBeNull();
        result.Data.TotalTrades.Should().Be(1);
    }

    [Fact]
    public async Task ActiveAlerts_ShouldReturnExpectedPagedStructure()
    {
        SetToken("ValidDashboardReadToken");
        var response = await _client.GetAsync("/api/dashboard/alerts?pageSize=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<Envelope<PagedResultDto<AlertDto>>>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("success");
        result.Data.Should().NotBeNull();
        result.Data.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RecentEvents_ShouldReturnExpectedPagedStructure()
    {
        SetToken("ValidDashboardReadToken");
        var response = await _client.GetAsync("/api/dashboard/events?pageSize=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<Envelope<PagedResultDto<RecentEventDto>>>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("success");
        result.Data.Should().NotBeNull();
        result.Data.Items.Should().NotBeEmpty();
    }

    [Fact]
    public async Task HealthHistory_ShouldReturnExpectedPagedStructure()
    {
        SetToken("ValidDashboardReadToken");
        var response = await _client.GetAsync("/api/dashboard/health/history?pageSize=5");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<Envelope<PagedResultDto<HealthHistoryRecordDto>>>();
        result.Should().NotBeNull();
        result!.Status.Should().Be("success");
        result.Data.Should().NotBeNull();
        result.Data.Items.Should().NotBeEmpty();
    }

    // --- 3. Validation Tests ---

    [Theory]
    [InlineData("/api/dashboard/positions?page=0")]
    [InlineData("/api/dashboard/positions?pageSize=0")]
    [InlineData("/api/dashboard/positions?pageSize=101")]
    [InlineData("/api/dashboard/trades?from=invalid-date")]
    [InlineData("/api/dashboard/trades?from=2026-08-10&to=2026-08-01")] // from > to
    public async Task InvalidParameters_ShouldBeRejected(string url)
    {
        SetToken("ValidDashboardReadToken");
        var response = await _client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var err = await response.Content.ReadFromJsonAsync<ErrorEnvelope>();
        err.Should().NotBeNull();
        err!.Status.Should().Be("error");
        err.Error.Code.Should().Be("VALIDATION_FAILED");
    }

    // --- 4. Security & Sanitization Tests ---

    [Fact]
    public async Task VerifyResponse_DoesNotExposeSensitiveData_InAlertsOrEvents()
    {
        SetToken("ValidDashboardReadToken");

        // Check alert message sanitization
        var alertResponse = await _client.GetAsync("/api/dashboard/alerts");
        var alertContent = await alertResponse.Content.ReadAsStringAsync();
        alertContent.Should().NotContain("SuperSecretPassword");
        alertContent.Should().Contain("[REDACTED]");

        // Check event message sanitization
        var eventResponse = await _client.GetAsync("/api/dashboard/events");
        var eventContent = await eventResponse.Content.ReadAsStringAsync();
        eventContent.Should().NotContain("BotTokenValue");
        eventContent.Should().Contain("[REDACTED]");
    }

    [Fact]
    public async Task VerifyErrorResponse_DoesNotExposeStackTrace_OnException()
    {
        SetToken("ValidDashboardReadToken");

        // Trigger a bad request or exception by throwing through bad query input
        var response = await _client.GetAsync("/api/dashboard/health?recentAlertsLimit=0"); // invalid limit
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotContain("StackTrace");
        content.Should().NotContain("Exception");
        content.Should().NotContain("SELECT");
    }

    // --- 5. Side-Effect Free Read-Only Verification Tests ---

    [Fact]
    public async Task VerifyGetRequests_AreStrictlyReadOnly_AndDoNotMutateState()
    {
        SetToken("ValidDashboardReadToken");

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TradingDbContext>();

        // Capture state counts before
        var ordersBefore = await context.Orders.CountAsync();
        var positionsBefore = await context.Positions.CountAsync();
        var tradesBefore = await context.Trades.CountAsync();
        var alertsBefore = await context.Alerts.CountAsync();

        // Perform multiple GET requests
        await _client.GetAsync("/api/dashboard/overview");
        await _client.GetAsync("/api/dashboard/health");
        await _client.GetAsync("/api/dashboard/trading");
        await _client.GetAsync("/api/dashboard/positions");
        await _client.GetAsync("/api/dashboard/orders");
        await _client.GetAsync("/api/dashboard/trades");

        // Capture state counts after
        var ordersAfter = await context.Orders.CountAsync();
        var positionsAfter = await context.Positions.CountAsync();
        var tradesAfter = await context.Trades.CountAsync();
        var alertsAfter = await context.Alerts.CountAsync();

        // Ensure absolutely no records changed
        ordersAfter.Should().Be(ordersBefore);
        positionsAfter.Should().Be(positionsBefore);
        tradesAfter.Should().Be(tradesBefore);
        alertsAfter.Should().Be(alertsBefore);
    }

    // Helper classes for contract mapping in tests

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
        public string CorrelationId { get; set; } = null!;
    }

    public class PagedResultDto<T>
    {
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public T[] Items { get; set; } = Array.Empty<T>();
    }

    public class PerformancePayload
    {
        public int TotalTrades { get; set; }
        public int WinningTrades { get; set; }
        public int LosingTrades { get; set; }
        public int BreakEvenTrades { get; set; }
        public decimal WinRate { get; set; }
        public decimal GrossPnL { get; set; }
        public decimal TotalFees { get; set; }
        public decimal NetPnL { get; set; }
    }
}
