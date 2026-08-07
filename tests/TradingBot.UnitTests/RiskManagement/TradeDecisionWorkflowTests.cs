using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TradingBot.Application.Repositories;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Application.RiskManagement.Workflow;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Domain.RiskManagement.Entities;
using TradingBot.Domain.RiskManagement.Enums;
using TradingBot.Domain.RiskManagement.ValueObjects;
using Xunit;

namespace TradingBot.UnitTests.RiskManagement;

public class TradeDecisionWorkflowTests
{
    private readonly Mock<IRiskRuleEngine> _mockRuleEngine = new();
    private readonly Mock<IRiskEvaluationRepository> _mockRiskEvalRepo = new();
    private readonly Mock<ITradeDecisionRepository> _mockTradeDecRepo = new();
    private readonly Mock<ISignalRepository> _mockSignalRepo = new();
    private readonly Mock<IUnitOfWork> _mockUow = new();
    private readonly Mock<IRiskAuditService> _mockAuditService = new();
    private readonly TradeDecisionWorkflow _workflow;

    public TradeDecisionWorkflowTests()
    {
        _workflow = new TradeDecisionWorkflow(
            NullLogger<TradeDecisionWorkflow>.Instance,
            _mockRuleEngine.Object,
            _mockRiskEvalRepo.Object,
            _mockTradeDecRepo.Object,
            _mockSignalRepo.Object,
            _mockUow.Object,
            _mockAuditService.Object
        );
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSuccessfullyEvaluateAndSave_WhenValidWorkflowRun()
    {
        // Arrange
        var signal = new Signal("BTCUSDT", SignalType.Buy, 60000m, 1m);
        var tradeRiskContext = new TradeRiskContext { SignalId = signal.Id, Symbol = "BTCUSDT", EntryPrice = 60000m, AccountBalance = 10000m };
        var workflowContext = new RiskWorkflowContext(signal, tradeRiskContext);

        var evaluation = new RiskEvaluation
        {
            SignalId = signal.Id,
            Decision = RiskDecisionStatus.Approved,
            Reason = "Passed rules"
        };

        // Mock repository setups
        _mockRiskEvalRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RiskEvaluation>());

        _mockRuleEngine.Setup(e => e.EvaluateAsync(It.IsAny<TradeRiskContext>()))
            .ReturnsAsync(evaluation);

        // Act
        var result = await _workflow.ExecuteAsync(workflowContext);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.RiskEvaluation.Should().Be(evaluation);
        result.TradeDecision.Should().NotBeNull();
        result.TradeDecision!.Decision.Should().Be(RiskDecisionStatus.Approved);

        // Verify status transitions
        signal.Status.Should().Be(SignalStatus.TradeApproved);

        // Verify transactions
        _mockUow.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        _mockUow.Verify(u => u.CommitTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);

        // Verify persistence calls
        _mockRiskEvalRepo.Verify(r => r.AddAsync(evaluation, It.IsAny<CancellationToken>()), Times.Once);
        _mockTradeDecRepo.Verify(r => r.AddAsync(It.IsAny<TradingBot.Domain.RiskManagement.Entities.TradeDecision>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockSignalRepo.Verify(r => r.Update(signal), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldBypassAndReturnDuplicate_WhenEvaluationAlreadyExists()
    {
        // Arrange
        var signal = new Signal("BTCUSDT", SignalType.Buy, 60000m, 1m);
        var tradeRiskContext = new TradeRiskContext { SignalId = signal.Id, Symbol = "BTCUSDT" };
        var workflowContext = new RiskWorkflowContext(signal, tradeRiskContext);

        var existingEval = new RiskEvaluation { SignalId = signal.Id, Decision = RiskDecisionStatus.Approved };
        var existingDecision = new TradingBot.Domain.RiskManagement.Entities.TradeDecision { SignalId = signal.Id, Decision = RiskDecisionStatus.Approved };

        _mockRiskEvalRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RiskEvaluation> { existingEval });
        _mockTradeDecRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TradingBot.Domain.RiskManagement.Entities.TradeDecision> { existingDecision });

        // Act
        var result = await _workflow.ExecuteAsync(workflowContext);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Message.Should().Contain("Duplicate execution");
        result.RiskEvaluation.Should().Be(existingEval);
        result.TradeDecision.Should().Be(existingDecision);

        // Verify no transaction or rules started
        _mockUow.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        _mockRuleEngine.Verify(e => e.EvaluateAsync(It.IsAny<TradeRiskContext>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldRollbackAndReturnFailure_WhenPersistenceThrowsException()
    {
        // Arrange
        var signal = new Signal("BTCUSDT", SignalType.Buy, 60000m, 1m);
        var tradeRiskContext = new TradeRiskContext { SignalId = signal.Id, Symbol = "BTCUSDT" };
        var workflowContext = new RiskWorkflowContext(signal, tradeRiskContext);

        _mockRiskEvalRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RiskEvaluation>());

        _mockRuleEngine.Setup(e => e.EvaluateAsync(It.IsAny<TradeRiskContext>()))
            .ReturnsAsync(new RiskEvaluation { SignalId = signal.Id, Decision = RiskDecisionStatus.Approved });

        _mockRiskEvalRepo.Setup(r => r.AddAsync(It.IsAny<RiskEvaluation>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB Failure"));

        // Act
        var result = await _workflow.ExecuteAsync(workflowContext);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.RiskEvaluation.Should().BeNull();

        // Verify rollback was invoked
        _mockUow.Verify(u => u.RollbackTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
