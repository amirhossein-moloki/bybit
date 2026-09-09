using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradingBot.Application.Models;
using TradingBot.Application.Services;
using TradingBot.Application.RiskManagement.Workflow;
using TradingBot.Application.RiskManagement.Services;
using TradingBot.Application.RiskManagement.Engine;
using TradingBot.Application.RiskManagement.Rules;
using TradingBot.Application.RiskManagement.Configuration;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Application.Trading.Execution.Services;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Domain.Enums;
using TradingBot.Domain.RiskManagement.Enums;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;
using TradingBot.Persistence.UnitOfWork;
using Xunit;

namespace TradingBot.IntegrationTests.Services;

public class PersianForexSignalExecutionIntegrationTests : IAsyncLifetime
{
    private SqliteConnection? _sqliteConnection;

    public async Task InitializeAsync()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        await _sqliteConnection.OpenAsync();
        using var command = _sqliteConnection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (_sqliteConnection != null)
        {
            await _sqliteConnection.CloseAsync();
            await _sqliteConnection.DisposeAsync();
        }
    }

    private TradingDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseSqlite(_sqliteConnection!)
            .Options;

        var context = new TradingDbContext(options);
        context.Database.EnsureCreated();
        DatabaseSeeder.SeedAsync(context).GetAwaiter().GetResult();
        return context;
    }

    [Theory]
    [InlineData(@"💎 سیگنال

📊 جفت ارز: GBP/USD


📉 نوع معامله: فروش ( SELL)

📍 نقطه ورود: 1.35460

🎯 حد سود

تارگت اول: 1.35350

تارگت دوم: 1.35210

تارگت سوم: 1.35050

🛑 حد ضرر (Stop Loss): 1.35570

نهایتا 2 پیپ اسپرد صرفا روی نقطه ورود لحاظ گردد", "GBPUSDT", OrderSide.Sell, 1.35460, 1.35570)]
    [InlineData(@"💎 سیگنال

📊 جفت ارز: EUR/USD


📉 نوع معامله: خرید ( BUY)

📍 نقطه ورود: 1.16010

🎯 حد سود

تارگت اول: 1.16120

تارگت دوم: 1.16230

تارگت سوم: 1.16410

🛑 حد ضرر (Stop Loss): 1.15900

نهایتا 2 پیپ اسپرد صرفا روی نقطه ورود لحاظ گردد", "EURUSDT", OrderSide.Buy, 1.16010, 1.15900)]
    public async Task E2E_PersianForexSignal_ShouldFilter_Store_EvaluateRisk_AndExecuteOrder(
        string rawText, string expectedSymbol, OrderSide expectedSide, decimal expectedEntry, decimal expectedSl)
    {
        // 1. Arrange Services and DB
        using var context = CreateDbContext();
        var uow = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);
        var signalRepo = new SignalRepository(context);
        var orderRepo = new OrderRepository(context);
        var orderEventRepo = new OrderEventRepository(context);
        var evalRepo = new RiskEvaluationRepository(context);
        var decRepo = new TradeDecisionRepository(context);
        var logRepo = new SystemLogRepository(context);

        // Risk Engine setup
        var calcOptions = Options.Create(new RiskCalculationOptions { DefaultRiskPercent = 2.0m, RoundingPrecision = 8 });
        var riskAmountCalc = new TradingBot.Application.RiskManagement.Calculators.RiskAmountCalculator();
        var stopLossDistanceCalc = new TradingBot.Application.RiskManagement.Calculators.StopLossDistanceCalculator();
        var positionSizeCalc = new TradingBot.Application.RiskManagement.Calculators.PositionSizeCalculator(riskAmountCalc, stopLossDistanceCalc, calcOptions);
        var riskRewardCalc = new TradingBot.Application.RiskManagement.Calculators.RiskRewardCalculator(calcOptions);
        var calcService = new RiskCalculationService(riskAmountCalc, stopLossDistanceCalc, positionSizeCalc, riskRewardCalc, calcOptions);

        var engineOptions = Options.Create(new RiskManagementOptions { Enabled = true, DefaultProfile = "Balanced", MaximumLeverage = 50 });
        var decisionService = new RiskDecisionService();
        var ruleExecutor = new RiskRuleExecutor(NullLogger<RiskRuleExecutor>.Instance);
        var rules = new List<IRiskRule> { new MaximumLeverageRule(engineOptions) };
        var ruleEngine = new RiskRuleEngine(NullLogger<RiskRuleEngine>.Instance, engineOptions, rules, ruleExecutor, decisionService, calcService);
        var auditService = new RiskAuditService(logRepo, NullLogger<RiskAuditService>.Instance);

        var riskWorkflow = new TradeDecisionWorkflow(
            NullLogger<TradeDecisionWorkflow>.Instance, ruleEngine, evalRepo, decRepo, signalRepo, uow, auditService);

        // Execution Engine setup
        var validator = new OrderValidator();
        var builder = new OrderBuilder();
        var instrumentRules = new TestExchangeInstrumentRules();
        var mockGateway = new TestExchangeTradingGateway();
        var metrics = new ExecutionMetrics();
        var eventHandler = new ExecutionEventHandler(NullLogger<ExecutionEventHandler>.Instance, logRepo, metrics);
        var eventPublisher = new ExecutionEventPublisher(new[] { eventHandler });

        var executionService = new TradingExecutionService(
            validator, builder, mockGateway, instrumentRules, orderRepo, orderEventRepo, uow, NullLogger<TradingExecutionService>.Instance, metrics);

        var orchestrator = new TradeExecutionOrchestrator(
            validator, orderRepo, executionService, eventPublisher, uow, NullLogger<TradeExecutionOrchestrator>.Instance);

        // Setup ServiceProvider for SignalStorageService pipeline resolution
        var services = new ServiceCollection();
        services.AddSingleton<ITradeDecisionWorkflow>(riskWorkflow);
        services.AddSingleton<ITradeExecutionOrchestrator>(orchestrator);
        var serviceProvider = services.BuildServiceProvider();

        var messageFilter = new MessageFilterService(NullLogger<MessageFilterService>.Instance, Options.Create(new SignalDetectionSettings()));
        var telegramDto = new TradingBot.Telegram.Models.TelegramMessageDto
        {
            ChannelId = 123456,
            MessageId = 7890,
            Text = rawText,
            Date = DateTime.UtcNow
        };

        // 2. Act - Message Analysis by MessageFilterService
        var candidate = await messageFilter.AnalyzeAsync(telegramDto);
        candidate.Should().NotBeNull();
        candidate!.DetectedSymbol.Should().Be(expectedSymbol);

        // 3. Act - Signal Storage and Pipeline Execution
        var storageMetrics = new SignalStorageMetrics();
        var storageService = new SignalStorageService(
            signalRepo, uow, storageMetrics, NullLogger<SignalStorageService>.Instance,
            serviceProvider: serviceProvider
        );

        await storageService.StoreAsync(candidate);

        // 4. Assert - Verify Signal was saved, parsed, evaluated, and executed
        var savedSignal = await context.Signals.FirstOrDefaultAsync(s => s.TelegramChannelId == candidate.ChannelId && s.TelegramMessageId == candidate.MessageId);
        savedSignal.Should().NotBeNull();
        savedSignal!.Symbol.Should().Be(expectedSymbol);
        savedSignal.Side.Should().Be(expectedSide);
        savedSignal.EntryPrice.Should().Be(expectedEntry);
        savedSignal.StopLoss.Should().Be(expectedSl);
        savedSignal.Status.Should().Be(SignalStatus.Executed);

        // Verify Order creation on exchange
        var savedOrder = await context.Orders.FirstOrDefaultAsync(o => o.SignalId == savedSignal.Id);
        savedOrder.Should().NotBeNull();
        savedOrder!.Status.Should().Be(OrderStatus.Filled);
        savedOrder.Symbol.Value.Should().Be(expectedSymbol);
    }
}
