using System;
using System.Collections.Generic;
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

public class AnalyticsProductionReadinessTests : IClassFixture<CustomWebApplicationFactory>, IAsyncLifetime
{
    private readonly CustomWebApplicationFactory _factory;
    private HttpClient _client = null!;

    public AnalyticsProductionReadinessTests(CustomWebApplicationFactory factory)
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
        context.Signals.RemoveRange(context.Signals);
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
    public async Task REQ_11_501_FullWorkflowIntegration_ShouldExecuteSuccessfully()
    {
        SetToken("ValidDashboardReadToken");

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TradingDbContext>();

        var baseTime = DateTime.UtcNow;

        // 1. Trade Created / Order Filled
        var order = new Order("WORKFLOW-O1", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1m), new Money(60000m));
        order.UpdateStatus(OrderStatus.Filled);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        // 2. Position Closed
        var position = new Position(order.Id, "BTCUSDT", OrderSide.Buy, 60000m, 1m, margin: 120m, initialStatus: PositionStatus.Closed);
        context.Positions.Add(position);
        await context.SaveChangesAsync();

        // 3. Trade Record Generated (completed realized trade)
        var trade = new Trade(
            position.Id,
            entryPrice: 60000m,
            exitPrice: 62000m,
            quantity: 1m,
            grossPnL: 2000m,
            tradingFee: 10m,
            fundingFee: 0m,
            netPnL: 1990m,
            closeReason: CloseReason.TakeProfit,
            openedAt: baseTime.AddHours(-2),
            closedAt: baseTime.AddHours(-1)
        );
        context.Trades.Add(trade);
        await context.SaveChangesAsync();

        // 4. Analytics Processing / Performance Metrics Generated
        var metricsResponse = await _client.GetAsync("/api/analytics/performance");
        metricsResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var metricsEnvelope = await metricsResponse.Content.ReadFromJsonAsync<Envelope<PerformanceMetricsDto>>();
        metricsEnvelope.Should().NotBeNull();
        metricsEnvelope!.Status.Should().Be("success");
        metricsEnvelope.Data.TotalTrades.Should().Be(1);
        metricsEnvelope.Data.NetPnL.Should().Be(1990m);

        // 5. Report Created
        var reportResponse = await _client.GetAsync("/api/analytics/report?initialBalance=10000");
        reportResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var reportEnvelope = await reportResponse.Content.ReadFromJsonAsync<Envelope<PerformanceReportDto>>();
        reportEnvelope.Should().NotBeNull();
        reportEnvelope!.Data.FinalBalance.Should().Be(11990m);

        // 6. Export Generated
        var exportResponse = await _client.GetAsync("/api/analytics/export/csv");
        exportResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var csv = await exportResponse.Content.ReadAsStringAsync();
        csv.Should().Contain("BTCUSDT");
        csv.Should().Contain("TakeProfit");
        csv.Should().Contain("1990.00000000");
    }

    [Fact]
    public async Task REQ_11_502_ValidateDataConsistency_ShouldMatchExactly()
    {
        SetToken("ValidDashboardReadToken");

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TradingDbContext>();

        var baseTime = DateTime.UtcNow;

        // Add 3 trades
        for (int i = 1; i <= 3; i++)
        {
            var order = new Order($"CONSIST-{i}", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1m), new Money(50000m));
            order.UpdateStatus(OrderStatus.Filled);
            context.Orders.Add(order);
            await context.SaveChangesAsync();

            var pos = new Position(order.Id, "BTCUSDT", OrderSide.Buy, 50000m, 1m, margin: 100m, initialStatus: PositionStatus.Closed);
            context.Positions.Add(pos);
            await context.SaveChangesAsync();

            var trade = new Trade(
                pos.Id,
                entryPrice: 50000m,
                exitPrice: 51000m,
                quantity: 1m,
                grossPnL: 1000m,
                tradingFee: 10m,
                fundingFee: 0m,
                netPnL: 990m,
                closeReason: CloseReason.TakeProfit,
                openedAt: baseTime.AddMinutes(-30 * i),
                closedAt: baseTime.AddMinutes(-10 * i)
            );
            context.Trades.Add(trade);
        }
        await context.SaveChangesAsync();

        // Query Stats
        var statsResponse = await _client.GetAsync("/api/analytics/overview");
        statsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var statsEnvelope = await statsResponse.Content.ReadFromJsonAsync<Envelope<TradeStatisticsDto>>();

        statsEnvelope!.Data.TotalTrades.Should().Be(3);
        statsEnvelope.Data.WinningTrades.Should().Be(3);
        statsEnvelope.Data.LosingTrades.Should().Be(0);
        statsEnvelope.Data.NetPnL.Should().Be(2970m);

        // Query Performance API
        var perfResponse = await _client.GetAsync("/api/analytics/performance");
        var perfEnvelope = await perfResponse.Content.ReadFromJsonAsync<Envelope<PerformanceMetricsDto>>();
        perfEnvelope!.Data.TotalTrades.Should().Be(3);
        perfEnvelope.Data.NetPnL.Should().Be(2970m);

        // Query Aggregation API
        var aggResponse = await _client.GetAsync("/api/analytics/aggregation?period=Daily");
        var aggEnvelope = await aggResponse.Content.ReadFromJsonAsync<Envelope<PeriodAggregationDto[]>>();
        aggEnvelope!.Data[0].TotalTrades.Should().Be(3);
        aggEnvelope.Data[0].NetPnL.Should().Be(2970m);
    }

    [Fact]
    public async Task REQ_11_503_ValidatePnLAccuracy_WinningLosingAndMultipleTrades()
    {
        SetToken("ValidDashboardReadToken");

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TradingDbContext>();
        var baseTime = DateTime.UtcNow;

        // --- Winning Trade ---
        // Entry: 60000, Exit: 62000, Quantity: 1, Fee: 10. Net Profit = Gross Profit - Fee = 1990
        var oWin = new Order("WIN-O1", new SymbolValueObject("BTCUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1m), new Money(60000m));
        oWin.UpdateStatus(OrderStatus.Filled);
        context.Orders.Add(oWin);
        await context.SaveChangesAsync();

        var posWin = new Position(oWin.Id, "BTCUSDT", OrderSide.Buy, 60000m, 1m, margin: 100m, initialStatus: PositionStatus.Closed);
        context.Positions.Add(posWin);
        await context.SaveChangesAsync();

        var tWin = new Trade(
            posWin.Id,
            entryPrice: 60000m,
            exitPrice: 62000m,
            quantity: 1m,
            grossPnL: 2000m,
            tradingFee: 10m,
            fundingFee: 0m,
            netPnL: 1990m,
            closeReason: CloseReason.TakeProfit,
            openedAt: baseTime.AddHours(-5),
            closedAt: baseTime.AddHours(-4)
        );
        context.Trades.Add(tWin);
        await context.SaveChangesAsync();

        // Check Winning Trade PnL alone
        var resWin = await _client.GetAsync($"/api/analytics/pnl?symbol=BTCUSDT");
        resWin.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelopeWin = await resWin.Content.ReadFromJsonAsync<Envelope<PnLSummary>>();
        envelopeWin!.Data.NetPnL.Should().Be(1990m);
        envelopeWin.Data.GrossProfit.Should().Be(1990m);
        envelopeWin.Data.GrossLoss.Should().Be(0m);

        // --- Losing Trade ---
        // Entry: 60000, Exit: 58000, Quantity: 1, Fee: 10. Net Profit = -2000 - 10 = -2010
        var oLoss = new Order("LOSS-O1", new SymbolValueObject("ETHUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(1m), new Money(60000m));
        oLoss.UpdateStatus(OrderStatus.Filled);
        context.Orders.Add(oLoss);
        await context.SaveChangesAsync();

        var posLoss = new Position(oLoss.Id, "ETHUSDT", OrderSide.Buy, 60000m, 1m, margin: 100m, initialStatus: PositionStatus.Closed);
        context.Positions.Add(posLoss);
        await context.SaveChangesAsync();

        var tLoss = new Trade(
            posLoss.Id,
            entryPrice: 60000m,
            exitPrice: 58000m,
            quantity: 1m,
            grossPnL: -2000m,
            tradingFee: 10m,
            fundingFee: 0m,
            netPnL: -2010m,
            closeReason: CloseReason.StopLoss,
            openedAt: baseTime.AddHours(-3),
            closedAt: baseTime.AddHours(-2)
        );
        context.Trades.Add(tLoss);
        await context.SaveChangesAsync();

        // Check Losing Trade PnL alone
        var resLoss = await _client.GetAsync($"/api/analytics/pnl?symbol=ETHUSDT");
        resLoss.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelopeLoss = await resLoss.Content.ReadFromJsonAsync<Envelope<PnLSummary>>();
        envelopeLoss!.Data.NetPnL.Should().Be(-2010m);
        envelopeLoss.Data.GrossLoss.Should().Be(2010m); // represented as positive magnitude

        // --- Multiple Trades ---
        // Total Net PnL = 1990 + (-2010) = -20
        var resAll = await _client.GetAsync("/api/analytics/pnl");
        resAll.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelopeAll = await resAll.Content.ReadFromJsonAsync<Envelope<PnLSummary>>();
        envelopeAll!.Data.NetPnL.Should().Be(-20m);
        envelopeAll.Data.GrossProfit.Should().Be(1990m);
        envelopeAll.Data.GrossLoss.Should().Be(2010m);
        envelopeAll.Data.ProfitFactor.Should().BeApproximately(1990m / 2010m, 0.0001m);
    }

    [Fact]
    public async Task REQ_11_504_ValidateAnalyticsAPIs_AuthorizationAndResponseSchema()
    {
        // 1. Unauthenticated should fail with 401
        ClearToken();
        var unauthRes = await _client.GetAsync("/api/analytics/overview");
        unauthRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // 2. Unauthorized (no permission) should fail with 403
        SetToken("ValidDashboardNoReadToken");
        var forbiddenRes = await _client.GetAsync("/api/analytics/overview");
        forbiddenRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 3. Authenticated should succeed
        SetToken("ValidDashboardReadToken");

        // Seed a quick trade to verify schema fields
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<TradingDbContext>();
        var order = new Order("SCHEMA-O1", new SymbolValueObject("SOLUSDT"), OrderSide.Buy, OrderType.Limit, new Quantity(5m), new Money(100m));
        order.UpdateStatus(OrderStatus.Filled);
        context.Orders.Add(order);
        await context.SaveChangesAsync();

        var pos = new Position(order.Id, "SOLUSDT", OrderSide.Buy, 100m, 5m, margin: 50m, initialStatus: PositionStatus.Closed);
        context.Positions.Add(pos);
        await context.SaveChangesAsync();

        var trade = new Trade(pos.Id, 100m, 105m, 5m, grossPnL: 25m, tradingFee: 1m, fundingFee: 0m, netPnL: 24m, CloseReason.TakeProfit, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow);
        context.Trades.Add(trade);
        await context.SaveChangesAsync();

        // GET /api/analytics/overview
        var resOverview = await _client.GetAsync("/api/analytics/overview");
        resOverview.StatusCode.Should().Be(HttpStatusCode.OK);
        var envOverview = await resOverview.Content.ReadFromJsonAsync<Envelope<TradeStatisticsDto>>();
        envOverview!.Status.Should().Be("success");
        envOverview.Data.TotalTrades.Should().Be(1);
        envOverview.Data.WinRate.Should().Be(100m);

        // GET /api/analytics/performance
        var resPerf = await _client.GetAsync("/api/analytics/performance");
        resPerf.StatusCode.Should().Be(HttpStatusCode.OK);
        var envPerf = await resPerf.Content.ReadFromJsonAsync<Envelope<PerformanceMetricsDto>>();
        envPerf!.Data.NetPnL.Should().Be(24m);

        // GET /api/analytics/pnl
        var resPnl = await _client.GetAsync("/api/analytics/pnl");
        resPnl.StatusCode.Should().Be(HttpStatusCode.OK);
        var envPnl = await resPnl.Content.ReadFromJsonAsync<Envelope<PnLSummary>>();
        envPnl!.Data.NetPnL.Should().Be(24m);

        // GET /api/analytics/symbols
        var resSymbols = await _client.GetAsync("/api/analytics/symbols");
        resSymbols.StatusCode.Should().Be(HttpStatusCode.OK);
        var envSymbols = await resSymbols.Content.ReadFromJsonAsync<Envelope<dynamic[]>>();
        envSymbols!.Data.Should().NotBeEmpty();

        // GET /api/analytics/signals
        var resSignals = await _client.GetAsync("/api/analytics/signals");
        resSignals.StatusCode.Should().Be(HttpStatusCode.OK);
        var envSignals = await resSignals.Content.ReadFromJsonAsync<Envelope<dynamic[]>>();
        envSignals!.Data.Should().NotBeEmpty();

        // GET /api/analytics/equity
        var resEquity = await _client.GetAsync("/api/analytics/equity");
        resEquity.StatusCode.Should().Be(HttpStatusCode.OK);
        var envEquity = await resEquity.Content.ReadFromJsonAsync<Envelope<EquityPointDto[]>>();
        envEquity!.Data.Should().NotBeEmpty();
    }

    [Fact]
    public async Task REQ_11_505_ValidateDashboardIntegration_AndBackwardCompatibility()
    {
        SetToken("ValidDashboardReadToken");

        // Validate date filtering on /overview
        var resFiltered = await _client.GetAsync("/api/analytics/overview?startDate=2020-01-01&endDate=2020-12-31");
        resFiltered.StatusCode.Should().Be(HttpStatusCode.OK);
        var envFiltered = await resFiltered.Content.ReadFromJsonAsync<Envelope<TradeStatisticsDto>>();
        envFiltered!.Data.TotalTrades.Should().Be(0); // empty in this date range

        // Validate invalid date range returns 400
        var resInvalidDate = await _client.GetAsync("/api/analytics/overview?startDate=2021-01-01&endDate=2020-01-01");
        resInvalidDate.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Validate bad parameter error response schema
        var resBadSide = await _client.GetAsync("/api/analytics/overview?side=InvalidSideValue");
        resBadSide.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var badResponseStr = await resBadSide.Content.ReadAsStringAsync();
        badResponseStr.Should().Contain("VALIDATION_FAILED");
    }

    [Fact]
    public async Task REQ_11_508_SecurityChecks_ShouldNotLeakCredentials()
    {
        SetToken("ValidDashboardReadToken");

        // 1. Check API json response doesn't contain secrets
        var response = await _client.GetAsync("/api/analytics/report");
        var jsonStr = await response.Content.ReadAsStringAsync();

        jsonStr.Should().NotContain("ApiKey");
        jsonStr.Should().NotContain("ApiSecret");
        jsonStr.Should().NotContain("TelegramToken");
        jsonStr.Should().NotContain("Password");

        // 2. Check CSV export doesn't leak secrets
        var csvResponse = await _client.GetAsync("/api/analytics/export/csv");
        var csvStr = await csvResponse.Content.ReadAsStringAsync();

        csvStr.Should().NotContain("ApiKey");
        csvStr.Should().NotContain("ApiSecret");
        csvStr.Should().NotContain("TelegramToken");
        csvStr.Should().NotContain("Password");
    }

    [Fact]
    public async Task FailureScenarios_InvalidQueries_ShouldReturnBadRequest()
    {
        SetToken("ValidDashboardReadToken");

        // 1. Initial balance <= 0
        var res1 = await _client.GetAsync("/api/analytics/report?initialBalance=-500");
        res1.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 2. Invalid date format
        var res2 = await _client.GetAsync("/api/analytics/report?startDate=NotADate");
        res2.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // 3. Invalid side parameter
        var res3 = await _client.GetAsync("/api/analytics/report?side=NotASide");
        res3.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    public class Envelope<T>
    {
        public string Status { get; set; } = null!;
        public T Data { get; set; } = default!;
    }

    public class PnLSummary
    {
        public decimal GrossProfit { get; set; }
        public decimal GrossLoss { get; set; }
        public decimal NetPnL { get; set; }
        public decimal AveragePnL { get; set; }
        public decimal ProfitFactor { get; set; }
    }
}
