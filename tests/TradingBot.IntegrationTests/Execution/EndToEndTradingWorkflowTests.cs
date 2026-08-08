using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TradingBot.Application.Repositories;
using TradingBot.Application.RiskManagement.Workflow;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Application.RiskManagement.Services;
using TradingBot.Application.RiskManagement.Engine;
using TradingBot.Application.RiskManagement.Rules;
using TradingBot.Application.RiskManagement.Configuration;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Models;
using TradingBot.Application.Trading.Execution.Services;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.RiskManagement.Enums;
using TradingBot.Domain.RiskManagement.ValueObjects;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;
using TradingBot.Persistence.UnitOfWork;
using Xunit;

namespace TradingBot.IntegrationTests.Execution;

public class EndToEndTradingWorkflowTests : IAsyncLifetime
{
    private SqliteConnection? _sqliteConnection;
    private TradingDbContext? _dbContext;

    public async Task InitializeAsync()
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        await _sqliteConnection.OpenAsync();
        using var command = _sqliteConnection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync();

        var options = new DbContextOptionsBuilder<TradingDbContext>()
            .UseSqlite(_sqliteConnection)
            .Options;

        _dbContext = new TradingDbContext(options);
        await _dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (_dbContext != null)
        {
            await _dbContext.DisposeAsync();
        }

        if (_sqliteConnection != null)
        {
            await _sqliteConnection.CloseAsync();
            await _sqliteConnection.DisposeAsync();
        }
    }

    [Fact]
    public async Task FullPipeline_FromSignal_ToRiskApproval_ToExecution_ShouldSucceedAndRecordAudit()
    {
        // 1. Arrange - Setup Database, Repositories, and Unit of Work
        var context = _dbContext!;
        var uow = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);
        var signalRepo = new SignalRepository(context);
        var orderRepo = new OrderRepository(context);
        var orderEventRepo = new OrderEventRepository(context);
        var evalRepo = new RiskEvaluationRepository(context);
        var decRepo = new TradeDecisionRepository(context);
        var logRepo = new SystemLogRepository(context);

        // 2. Arrange - Setup Risk Engine Components
        var calcOptions = Options.Create(new RiskCalculationOptions
        {
            DefaultRiskPercent = 2.0m,
            RoundingPrecision = 8
        });

        var riskAmountCalc = new TradingBot.Application.RiskManagement.Calculators.RiskAmountCalculator();
        var stopLossDistanceCalc = new TradingBot.Application.RiskManagement.Calculators.StopLossDistanceCalculator();
        var positionSizeCalc = new TradingBot.Application.RiskManagement.Calculators.PositionSizeCalculator(riskAmountCalc, stopLossDistanceCalc, calcOptions);
        var riskRewardCalc = new TradingBot.Application.RiskManagement.Calculators.RiskRewardCalculator(calcOptions);

        var calcService = new RiskCalculationService(
            riskAmountCalc,
            stopLossDistanceCalc,
            positionSizeCalc,
            riskRewardCalc,
            calcOptions
        );

        var engineOptions = Options.Create(new RiskManagementOptions
        {
            Enabled = true,
            DefaultProfile = "Balanced",
            MaximumLeverage = 10,
            AutoReduceLeverage = false
        });

        var decisionService = new RiskDecisionService();
        var ruleExecutor = new RiskRuleExecutor(NullLogger<RiskRuleExecutor>.Instance);
        var rules = new List<IRiskRule> { new MaximumLeverageRule(engineOptions) };

        var ruleEngine = new RiskRuleEngine(
            NullLogger<RiskRuleEngine>.Instance,
            engineOptions,
            rules,
            ruleExecutor,
            decisionService,
            calcService
        );

        var auditService = new RiskAuditService(logRepo, NullLogger<RiskAuditService>.Instance);

        var riskWorkflow = new TradeDecisionWorkflow(
            NullLogger<TradeDecisionWorkflow>.Instance,
            ruleEngine,
            evalRepo,
            decRepo,
            signalRepo,
            uow,
            auditService
        );

        // 3. Arrange - Setup Execution Engine Components
        var validator = new OrderValidator();
        var builder = new OrderBuilder();
        var instrumentRules = new TestExchangeInstrumentRules();
        var mockGateway = new TestExchangeTradingGateway(); // default returns success Filled

        var metrics = new ExecutionMetrics();
        var eventHandler = new ExecutionEventHandler(NullLogger<ExecutionEventHandler>.Instance, logRepo, metrics);
        var eventPublisher = new ExecutionEventPublisher(new[] { eventHandler });

        var executionService = new TradingExecutionService(
            validator,
            builder,
            mockGateway,
            instrumentRules,
            orderRepo,
            orderEventRepo,
            uow,
            NullLogger<TradingExecutionService>.Instance,
            metrics
        );

        var orchestrator = new TradeExecutionOrchestrator(
            validator,
            orderRepo,
            executionService,
            eventPublisher,
            uow,
            NullLogger<TradeExecutionOrchestrator>.Instance
        );

        // 4. Act - Create and Save Signal
        var signal = new Signal("Telegram", "BUY BTCUSDT entry 60000 sl 59000 tp 63000", "BTCUSDT", OrderSide.Buy, 60000m, 1m, 59000m, 63000m, 10);
        await signalRepo.AddAsync(signal);
        await uow.SaveChangesAsync();

        // 5. Act - Run Risk Management Workflow to get Approved Decision
        var tradeRiskContext = new TradeRiskContext
        {
            SignalId = signal.Id,
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            EntryPrice = 60000m,
            StopLoss = 59000m,
            TakeProfits = new List<decimal> { 63000m },
            Leverage = 10,
            AccountBalance = 10000m,
            OpenPositions = 0,
            DailyPnL = 0m,
            CurrentExposure = 0m
        };

        var workflowContext = new RiskWorkflowContext(signal, tradeRiskContext);
        var riskWorkflowResult = await riskWorkflow.ExecuteAsync(workflowContext);

        riskWorkflowResult.IsSuccess.Should().BeTrue();
        riskWorkflowResult.TradeDecision!.Decision.Should().Be(RiskDecisionStatus.Approved);

        // 6. Act - Run Execution Orchestration for Approved Decision
        var executionRequest = new TradeExecutionRequest
        {
            SignalId = signal.Id,
            RiskEvaluationId = riskWorkflowResult.RiskEvaluation!.Id,
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            OrderType = OrderType.Limit,
            Quantity = 0.5m,
            Price = 60000m,
            RiskDecision = RiskDecisionStatus.Approved
        };

        var executionResult = await orchestrator.OrchestrateAsync(executionRequest);

        // 7. Assert - Verify Execution Outcomes
        executionResult.Should().NotBeNull();
        executionResult.Success.Should().BeTrue();
        executionResult.Status.Should().Be(OrderStatus.Filled);
        executionResult.ExchangeOrderId.Should().NotBeNullOrEmpty();

        // Verify order persistence in SQL Database
        var savedOrder = await context.Orders.FirstOrDefaultAsync(o => o.SignalId == signal.Id);
        savedOrder.Should().NotBeNull();
        savedOrder!.Status.Should().Be(OrderStatus.Filled);
        savedOrder.ExchangeOrderId.Should().Be(executionResult.ExchangeOrderId);

        // Verify metrics tracking updates (Section 8)
        metrics.TotalExecutions.Should().Be(1);
        metrics.SuccessfulExecutions.Should().Be(1);
        metrics.FilledOrders.Should().Be(1);
        metrics.AverageExecutionTime.Should().BeGreaterThan(0);

        // Verify that events/audit logs are persisted in SystemLogs table (Section 7)
        var logs = await context.SystemLogs.ToListAsync();
        logs.Any(l => l.Message.Contains("TradeExecutionStarted")).Should().BeTrue();
        logs.Any(l => l.Message.Contains("OrderSubmissionStarted")).Should().BeTrue();
        logs.Any(l => l.Message.Contains("OrderFilled")).Should().BeTrue();
        logs.Any(l => l.Message.Contains("TradeExecutionCompleted")).Should().BeTrue();
    }
}
