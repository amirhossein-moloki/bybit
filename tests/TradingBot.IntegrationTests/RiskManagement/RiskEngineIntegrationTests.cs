using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TradingBot.Application.Repositories;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Application.RiskManagement.Services;
using TradingBot.Application.RiskManagement.Engine;
using TradingBot.Application.RiskManagement.Rules;
using TradingBot.Application.RiskManagement.Configuration;
using TradingBot.Domain.Enums;
using TradingBot.Domain.RiskManagement.Entities;
using TradingBot.Domain.RiskManagement.Enums;
using TradingBot.Domain.RiskManagement.ValueObjects;
using TradingBot.Persistence.Context;
using TradingBot.Persistence.Repositories;
using TradingBot.Persistence.UnitOfWork;
using Xunit;

namespace TradingBot.IntegrationTests.RiskManagement;

public class RiskEngineIntegrationTests : IAsyncLifetime
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
    public async Task EvaluateAsync_ShouldPerformCalculationsAndSaveRiskEvaluationToDatabase()
    {
        // Arrange
        var context = _dbContext!;
        var uow = new UnitOfWork(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<UnitOfWork>.Instance);
        var evaluationRepo = new RiskEvaluationRepository(context);

        var options = Microsoft.Extensions.Options.Options.Create(new TradingBot.Application.RiskManagement.Configuration.RiskCalculationOptions
        {
            DefaultRiskPercent = 2.0m, // 2% risk
            RoundingPrecision = 8
        });

        var riskAmountCalc = new TradingBot.Application.RiskManagement.Calculators.RiskAmountCalculator();
        var stopLossDistanceCalc = new TradingBot.Application.RiskManagement.Calculators.StopLossDistanceCalculator();
        var positionSizeCalc = new TradingBot.Application.RiskManagement.Calculators.PositionSizeCalculator(riskAmountCalc, stopLossDistanceCalc, options);
        var riskRewardCalc = new TradingBot.Application.RiskManagement.Calculators.RiskRewardCalculator(options);

        var calcService = new RiskCalculationService(
            riskAmountCalc,
            stopLossDistanceCalc,
            positionSizeCalc,
            riskRewardCalc,
            options
        );

        var engineOptions = Microsoft.Extensions.Options.Options.Create(new TradingBot.Application.RiskManagement.Configuration.RiskManagementOptions
        {
            Enabled = true,
            DefaultProfile = "Balanced"
        });

        var decisionService = new RiskDecisionService();
        var ruleExecutor = new RiskRuleExecutor(Microsoft.Extensions.Logging.Abstractions.NullLogger<RiskRuleExecutor>.Instance);
        var rules = Enumerable.Empty<IRiskRule>();

        var ruleEngine = new RiskRuleEngine(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RiskRuleEngine>.Instance,
            engineOptions,
            rules,
            ruleExecutor,
            decisionService,
            calcService
        );

        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<TradingBot.Infrastructure.RiskManagement.Services.RiskEngineService>.Instance;

        var riskEngine = new TradingBot.Infrastructure.RiskManagement.Services.RiskEngineService(
            logger,
            engineOptions,
            ruleEngine,
            evaluationRepo,
            uow
        );

        var signalId = Guid.NewGuid();
        var tradeRiskContext = new TradeRiskContext
        {
            SignalId = signalId,
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            EntryPrice = 60000m,
            StopLoss = 59000m,
            TakeProfits = new List<decimal> { 62000m, 65000m },
            Leverage = 10,
            AccountBalance = 10000m,
            OpenPositions = 0,
            DailyPnL = 0m,
            CurrentExposure = 0m
        };

        // Act
        var decision = await riskEngine.EvaluateAsync(tradeRiskContext);

        // Assert
        decision.Should().NotBeNull();
        decision.Decision.Should().Be(RiskDecisionStatus.Approved);

        // Verify that the record is stored correctly in the DB
        var savedEvaluation = await context.RiskEvaluations.FirstOrDefaultAsync(e => e.SignalId == signalId);
        savedEvaluation.Should().NotBeNull();
        savedEvaluation!.RiskAmount.Should().Be(200m); // 2% of 10000 = 200
        savedEvaluation.PositionSize.Should().Be(0.2m); // 200 / 1000 = 0.2 BTC
        savedEvaluation.RiskReward.Should().Be(3.5m); // Avg TP = 63500. Distance = 3500. SL distance = 1000. RR = 3.5
        savedEvaluation.Exposure.Should().Be(12000m); // 0.2 * 60000 = 12000
        savedEvaluation.Decision.Should().Be(RiskDecisionStatus.Approved);
        savedEvaluation.Reason.Should().Be("No risk rules executed.");
    }

    [Fact]
    public async Task EvaluateAsync_ShouldEvaluateRulesAndAggregateResults_WhenRulesFail()
    {
        // Arrange
        var context = _dbContext!;
        var uow = new UnitOfWork(context, Microsoft.Extensions.Logging.Abstractions.NullLogger<UnitOfWork>.Instance);
        var evaluationRepo = new RiskEvaluationRepository(context);

        var options = Microsoft.Extensions.Options.Options.Create(new TradingBot.Application.RiskManagement.Configuration.RiskCalculationOptions
        {
            DefaultRiskPercent = 2.0m,
            RoundingPrecision = 8
        });

        var riskAmountCalc = new TradingBot.Application.RiskManagement.Calculators.RiskAmountCalculator();
        var stopLossDistanceCalc = new TradingBot.Application.RiskManagement.Calculators.StopLossDistanceCalculator();
        var positionSizeCalc = new TradingBot.Application.RiskManagement.Calculators.PositionSizeCalculator(riskAmountCalc, stopLossDistanceCalc, options);
        var riskRewardCalc = new TradingBot.Application.RiskManagement.Calculators.RiskRewardCalculator(options);

        var calcService = new RiskCalculationService(
            riskAmountCalc,
            stopLossDistanceCalc,
            positionSizeCalc,
            riskRewardCalc,
            options
        );

        var engineOptions = Microsoft.Extensions.Options.Options.Create(new TradingBot.Application.RiskManagement.Configuration.RiskManagementOptions
        {
            Enabled = true,
            DefaultProfile = "Balanced",
            MaximumLeverage = 5, // We set leverage limit to 5
            AutoReduceLeverage = false
        });

        var decisionService = new RiskDecisionService();
        var ruleExecutor = new RiskRuleExecutor(Microsoft.Extensions.Logging.Abstractions.NullLogger<RiskRuleExecutor>.Instance);

        // Pass a leverage rule that is guaranteed to fail
        var rules = new List<IRiskRule>
        {
            new MaximumLeverageRule(engineOptions)
        };

        var ruleEngine = new RiskRuleEngine(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RiskRuleEngine>.Instance,
            engineOptions,
            rules,
            ruleExecutor,
            decisionService,
            calcService
        );

        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<TradingBot.Infrastructure.RiskManagement.Services.RiskEngineService>.Instance;

        var riskEngine = new TradingBot.Infrastructure.RiskManagement.Services.RiskEngineService(
            logger,
            engineOptions,
            ruleEngine,
            evaluationRepo,
            uow
        );

        var signalId = Guid.NewGuid();
        var tradeRiskContext = new TradeRiskContext
        {
            SignalId = signalId,
            Symbol = "BTCUSDT",
            Side = OrderSide.Buy,
            EntryPrice = 60000m,
            StopLoss = 59000m,
            TakeProfits = new List<decimal> { 62000m },
            Leverage = 10, // Exceeds limit 5
            AccountBalance = 10000m,
            OpenPositions = 0,
            DailyPnL = 0m,
            CurrentExposure = 0m
        };

        // Act
        var decision = await riskEngine.EvaluateAsync(tradeRiskContext);

        // Assert
        decision.Should().NotBeNull();
        decision.Decision.Should().Be(RiskDecisionStatus.Rejected);
        decision.Reason.Should().Contain("exceeds the maximum allowed limit");

        // Verify database entry
        var savedEvaluation = await context.RiskEvaluations.FirstOrDefaultAsync(e => e.SignalId == signalId);
        savedEvaluation.Should().NotBeNull();
        savedEvaluation!.Decision.Should().Be(RiskDecisionStatus.Rejected);
    }
}
