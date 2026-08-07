using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Repositories;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Application.RiskManagement.Services;
using TradingBot.Domain.RiskManagement.Entities;
using TradingBot.Domain.RiskManagement.Enums;

namespace TradingBot.Application.RiskManagement.Workflow;

public interface ITradeDecisionWorkflow
{
    Task<WorkflowResult> ExecuteAsync(RiskWorkflowContext context, CancellationToken cancellationToken = default);
}

public class TradeDecisionWorkflow : ITradeDecisionWorkflow
{
    private readonly ILogger<TradeDecisionWorkflow> _logger;
    private readonly IRiskRuleEngine _ruleEngine;
    private readonly IRiskEvaluationRepository _riskEvaluationRepository;
    private readonly ITradeDecisionRepository _tradeDecisionRepository;
    private readonly ISignalRepository _signalRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRiskAuditService _auditService;

    public TradeDecisionWorkflow(
        ILogger<TradeDecisionWorkflow> logger,
        IRiskRuleEngine ruleEngine,
        IRiskEvaluationRepository riskEvaluationRepository,
        ITradeDecisionRepository tradeDecisionRepository,
        ISignalRepository signalRepository,
        IUnitOfWork unitOfWork,
        IRiskAuditService auditService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ruleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
        _riskEvaluationRepository = riskEvaluationRepository ?? throw new ArgumentNullException(nameof(riskEvaluationRepository));
        _tradeDecisionRepository = tradeDecisionRepository ?? throw new ArgumentNullException(nameof(tradeDecisionRepository));
        _signalRepository = signalRepository ?? throw new ArgumentNullException(nameof(signalRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
    }

    public async Task<WorkflowResult> ExecuteAsync(RiskWorkflowContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Risk Workflow Started");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 1. Prevent duplicate processing
            var existingEvaluation = (await _riskEvaluationRepository.GetAllAsync(cancellationToken))
                .FirstOrDefault(e => e.SignalId == context.Signal.Id);

            if (existingEvaluation != null)
            {
                _logger.LogWarning("Duplicate processing detected: Risk evaluation already exists for SignalId {SignalId}", context.Signal.Id);
                var existingDecision = (await _tradeDecisionRepository.GetAllAsync(cancellationToken))
                    .FirstOrDefault(d => d.SignalId == context.Signal.Id);

                return WorkflowResult.Success(
                    context.Signal.Id,
                    "Duplicate execution ignored.",
                    existingEvaluation,
                    existingDecision!
                );
            }

            // 2. Start Single Transaction
            await _unitOfWork.BeginTransactionAsync(cancellationToken);

            // 3. Record: Evaluation Started
            await _auditService.RecordEvaluationStartedAsync(context.Signal.Id, cancellationToken);

            // 4. Update Signal status to RiskEvaluationStarted
            context.Signal.MarkRiskEvaluationStarted();
            _signalRepository.Update(context.Signal);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // 5. Run Calculation & Rules Engine
            RiskEvaluation evaluation;
            try
            {
                _logger.LogInformation("Calculation Completed");
                evaluation = await _ruleEngine.EvaluateAsync(context.TradeRiskContext);
                _logger.LogInformation("Rules Executed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Decision Generation Failure for signal {SignalId}", context.Signal.Id);

                // Fallback to NeedsManualReview
                evaluation = new RiskEvaluation
                {
                    SignalId = context.Signal.Id,
                    RiskAmount = 0m,
                    PositionSize = 0m,
                    RiskReward = 0m,
                    Exposure = context.TradeRiskContext.CurrentExposure,
                    Decision = RiskDecisionStatus.NeedsManualReview,
                    Reason = $"Calculation or Rule Engine failed with unexpected error: {ex.Message}",
                    RiskLevel = RiskLevel.Critical,
                    ExecutionTime = stopwatch.Elapsed,
                    ExecutedRules = Array.Empty<string>(),
                    PassedRules = Array.Empty<string>(),
                    FailedRules = Array.Empty<string>()
                };
            }

            _logger.LogInformation("Decision Generated");

            // 6. Update Signal Status
            context.Signal.MarkRiskEvaluated();
            if (evaluation.Decision == RiskDecisionStatus.Approved)
            {
                context.Signal.MarkTradeApproved();
            }
            else if (evaluation.Decision == RiskDecisionStatus.Rejected)
            {
                context.Signal.MarkTradeRejected();
            }
            else
            {
                context.Signal.MarkManualReview();
            }
            _signalRepository.Update(context.Signal);

            // 7. Persist Risk Evaluation
            await _riskEvaluationRepository.AddAsync(evaluation, cancellationToken);

            // 8. Create and persist Trade Decision Entity
            var tradeDecision = new TradeDecision
            {
                SignalId = context.Signal.Id,
                Decision = evaluation.Decision,
                DecisionReason = evaluation.Reason,
                RiskEvaluationId = evaluation.Id,
                Status = evaluation.Decision == RiskDecisionStatus.Approved ? "Approved" :
                         evaluation.Decision == RiskDecisionStatus.Rejected ? "Rejected" : "NeedsManualReview"
            };
            await _tradeDecisionRepository.AddAsync(tradeDecision, cancellationToken);

            stopwatch.Stop();

            // 9. Record Audits
            await _auditService.RecordRulesExecutedAsync(context.Signal.Id, evaluation.ExecutedRules, cancellationToken);
            if (evaluation.FailedRules.Any())
            {
                await _auditService.RecordRuleFailuresAsync(context.Signal.Id, evaluation.FailedRules, cancellationToken);
            }
            await _auditService.RecordFinalDecisionAsync(context.Signal.Id, evaluation.Decision, evaluation.Reason, cancellationToken);
            await _auditService.RecordProcessingDurationAsync(context.Signal.Id, stopwatch.Elapsed, cancellationToken);

            // Commit transaction
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync(cancellationToken);

            _logger.LogInformation("Evaluation Saved");
            _logger.LogInformation("Workflow Completed");

            return WorkflowResult.Success(context.Signal.Id, "Risk workflow completed successfully.", evaluation, tradeDecision);
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Critical Log: Unexpected exception in TradeDecisionWorkflow for SignalId {SignalId}", context.Signal.Id);

            try
            {
                await _unitOfWork.RollbackTransactionAsync(cancellationToken);
            }
            catch (Exception rbEx)
            {
                _logger.LogError(rbEx, "Failed to rollback transaction during workflow recovery.");
            }

            return WorkflowResult.Failure(context.Signal.Id, $"Workflow failed: {ex.Message}", ex);
        }
    }
}
