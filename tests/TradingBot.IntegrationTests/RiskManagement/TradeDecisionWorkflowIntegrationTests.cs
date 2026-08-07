using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TradingBot.Application.Repositories;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Application.RiskManagement.Workflow;
using TradingBot.Application.RiskManagement.Services;
using TradingBot.Application.RiskManagement.Engine;
using TradingBot.Application.RiskManagement.Rules;
using TradingBot.Application.RiskManagement.Configuration;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.RiskManagement.Entities;
using TradingBot.Domain.RiskManagement.Enums;
using TradingBot.Domain.RiskManagement.ValueObjects;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;
using TradingBot.Persistence.UnitOfWork;
using Xunit;

namespace TradingBot.IntegrationTests.RiskManagement;

public class TradeDecisionWorkflowIntegrationTests : IAsyncLifetime
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
    public async Task ExecuteAsync_ShouldCompleteCompletePipeline_AndPersistAllEntitiesSuccessfully()
    {
        // Arrange
        var context = _dbContext!;
        var uow = new UnitOfWork(context, NullLogger<UnitOfWork>.Instance);
        var signalRepo = new SignalRepository(context);
        var evalRepo = new RiskEvaluationRepository(context);
        var decRepo = new TradeDecisionRepository(context);
        var logRepo = new SystemLogRepository(context);

        var calcOptions = Microsoft.Extensions.Options.Options.Create(new RiskCalculationOptions
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

        var engineOptions = Microsoft.Extensions.Options.Options.Create(new RiskManagementOptions
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

        var workflow = new TradeDecisionWorkflow(
            NullLogger<TradeDecisionWorkflow>.Instance,
            ruleEngine,
            evalRepo,
            decRepo,
            signalRepo,
            uow,
            auditService
        );

        // Add pre-existing Signal entity to Db
        var signal = new Signal("Telegram", "BUY BTCUSDT entry 60000 sl 59000", "BTCUSDT", OrderSide.Buy, 60000m, 1m, 59000m, 63000m, 10);
        await signalRepo.AddAsync(signal);
        await uow.SaveChangesAsync();

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

        // Act
        var result = await workflow.ExecuteAsync(workflowContext);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.RiskEvaluation.Should().NotBeNull();
        result.TradeDecision.Should().NotBeNull();

        // 1. Verify Trade Decision Entity
        var savedDecision = await context.TradeDecisions.FirstOrDefaultAsync(d => d.SignalId == signal.Id);
        savedDecision.Should().NotBeNull();
        savedDecision!.Decision.Should().Be(RiskDecisionStatus.Approved);
        savedDecision.Status.Should().Be("Approved");

        // 2. Verify Risk Evaluation Entity is updated and saved
        var savedEval = await context.RiskEvaluations.FirstOrDefaultAsync(e => e.SignalId == signal.Id);
        savedEval.Should().NotBeNull();
        savedEval!.Decision.Should().Be(RiskDecisionStatus.Approved);
        savedEval.ExecutedRules.Should().Contain("MaximumLeverageRule");

        // 3. Verify Signal status transition
        var savedSignal = await context.Signals.FirstOrDefaultAsync(s => s.Id == signal.Id);
        savedSignal.Should().NotBeNull();
        savedSignal!.Status.Should().Be(SignalStatus.TradeApproved);

        // 4. Verify Immutable Audit Trail records are saved in DB System Logs
        var auditLogs = await context.SystemLogs.ToListAsync();
        foreach (var log in auditLogs)
        {
            Console.WriteLine($"DB LOG: Category={log.Category}, Message={log.Message}");
        }
        var auditLogsFiltered = auditLogs.Where(l => l.Category == "Audit").ToList();
        auditLogsFiltered.Should().NotBeEmpty();
        auditLogsFiltered.Any(l => l.Message.Contains("Evaluation Started")).Should().BeTrue();
        auditLogsFiltered.Any(l => l.Message.Contains("Rules Executed")).Should().BeTrue();
        auditLogsFiltered.Any(l => l.Message.Contains("Final Decision")).Should().BeTrue();
    }
}
