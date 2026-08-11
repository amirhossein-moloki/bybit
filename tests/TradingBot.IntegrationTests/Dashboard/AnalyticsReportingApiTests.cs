using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.Application.Analytics.DTOs;
using TradingBot.Application.Analytics.Interfaces;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.ValueObjects;
using TradingBot.Persistence.Context;
using Xunit;

using SymbolValueObject = TradingBot.Domain.ValueObjects.Symbol;

namespace TradingBot.IntegrationTests.Dashboard;

public class AnalyticsReportingApiTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private HttpClient _client = null!;

    public AnalyticsReportingApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TradingDbContext>();

        await context.Database.EnsureCreatedAsync();

        // Clear existing tables
        context.Orders.RemoveRange(context.Orders);
        context.Positions.RemoveRange(context.Positions);
        context.Trades.RemoveRange(context.Trades);
        context.ReportSchedules.RemoveRange(context.ReportSchedules);
        await context.SaveChangesAsync();

        var baseTime = DateTime.UtcNow;

        // Seed 1 completed trade
        var o1 = new Order("REP-O1", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1m), new Money(50000m));
        o1.UpdateStatus(OrderStatus.Filled);
        context.Orders.Add(o1);
        await context.SaveChangesAsync();

        var pos1 = new Position(o1.Id, "BTCUSDT", OrderSide.Buy, 50000m, 1m, margin: 100m, initialStatus: PositionStatus.Closed);
        context.Positions.Add(pos1);
        await context.SaveChangesAsync();

        var t1 = new Trade(
            pos1.Id,
            entryPrice: 50000m,
            exitPrice: 51000m,
            quantity: 1m,
            grossPnL: 1000m,
            tradingFee: 10m,
            fundingFee: 0m,
            netPnL: 990m,
            closeReason: CloseReason.TakeProfit,
            openedAt: baseTime.AddHours(-1),
            closedAt: baseTime
        );
        context.Trades.Add(t1);
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

    [Fact]
    public async Task AnalyticsReport_Unauthenticated_ShouldReturn401()
    {
        ClearToken();

        var response = await _client.GetAsync("/api/analytics/report");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetReport_Authenticated_ShouldReturnFullPerformanceReport()
    {
        SetToken("ValidDashboardReadToken");

        var response = await _client.GetAsync("/api/analytics/report?initialBalance=10000");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await response.Content.ReadFromJsonAsync<Envelope<PerformanceReportDto>>();
        envelope.Should().NotBeNull();
        envelope!.Status.Should().Be("success");
        envelope.Data.Should().NotBeNull();
        envelope.Data.InitialBalance.Should().Be(10000m);
        envelope.Data.FinalBalance.Should().Be(10990m);
        envelope.Data.Metrics.TotalTrades.Should().Be(1);
        envelope.Data.DetailedTrades.Should().HaveCount(1);
        envelope.Data.EquityCurve.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetEquityCurve_Authenticated_ShouldReturnPoints()
    {
        SetToken("ValidDashboardReadToken");

        var response = await _client.GetAsync("/api/analytics/equity-curve?initialBalance=10000");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await response.Content.ReadFromJsonAsync<Envelope<EquityPointDto[]>>();
        envelope.Should().NotBeNull();
        envelope!.Status.Should().Be("success");
        envelope.Data.Should().HaveCount(1);
        envelope.Data[0].Equity.Should().Be(10990m);
        envelope.Data[0].CumulativePnL.Should().Be(990m);
    }

    [Fact]
    public async Task GetAggregation_Authenticated_ShouldReturnAggregates()
    {
        SetToken("ValidDashboardReadToken");

        var response = await _client.GetAsync("/api/analytics/aggregation?period=Daily");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await response.Content.ReadFromJsonAsync<Envelope<PeriodAggregationDto[]>>();
        envelope.Should().NotBeNull();
        envelope!.Status.Should().Be("success");
        envelope.Data.Should().HaveCount(1);
        envelope.Data[0].TotalTrades.Should().Be(1);
        envelope.Data[0].NetPnL.Should().Be(990m);
    }

    [Fact]
    public async Task ExportCsv_Authenticated_ShouldReturnTextCsv()
    {
        SetToken("ValidDashboardReadToken");

        var response = await _client.GetAsync("/api/analytics/export/csv");
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var content = await response.Content.ReadAsStringAsync();
            throw new Exception($"Request failed with status {response.StatusCode}. Response: {content}");
        }
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/csv");

        var csv = await response.Content.ReadAsStringAsync();
        csv.Should().Contain("TradeId,PositionId,Symbol,Side,EntryPrice,ExitPrice,Quantity,GrossPnL,Fee,FundingFee,NetPnL,CloseReason,OpenedAt,ClosedAt");
        csv.Should().Contain("BTCUSDT");
        csv.Should().Contain("TakeProfit");
    }

    [Fact]
    public async Task DebugQueryService_StreamReportTradesAsync_ShouldNotThrow()
    {
        using var scope = _factory.Services.CreateScope();
        var qService = scope.ServiceProvider.GetRequiredService<IAnalyticsReportingQueryService>();
        try
        {
            var list = new List<ReportTradeDto>();
            await foreach (var item in qService.StreamReportTradesAsync(new ReportFilterDto()))
            {
                list.Add(item);
            }
            list.Should().NotBeEmpty();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"QUERY_SERVICE_ERROR: {ex}");
            throw;
        }
    }

    [Fact]
    public async Task SaveSchedule_Authenticated_ShouldSaveAndReturnDto()
    {
        SetToken("ValidDashboardReadToken");

        var dto = new ReportScheduleDto(null, "E2E Schedule", "0 12 * * *", "Weekly", "recipient@e2e.com", "CSV", true);

        var response = await _client.PostAsJsonAsync("/api/analytics/schedule", dto);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var envelope = await response.Content.ReadFromJsonAsync<Envelope<ReportScheduleDto>>();
        envelope.Should().NotBeNull();
        envelope!.Status.Should().Be("success");
        envelope.Data.Id.Should().NotBeNull();
        envelope.Data.ScheduleName.Should().Be("E2E Schedule");
    }

    public class Envelope<T>
    {
        public string Status { get; set; } = null!;
        public T Data { get; set; } = default!;
    }
}
