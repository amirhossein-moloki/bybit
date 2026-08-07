using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBot.Application.RiskManagement.Exceptions;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Application.RiskManagement.Models;
using TradingBot.Application.RiskManagement.Services;
using TradingBot.Domain.RiskManagement.Entities;
using TradingBot.Domain.RiskManagement.Enums;
using TradingBot.Domain.RiskManagement.ValueObjects;
using TradingBot.Application.RiskManagement.Configuration;

namespace TradingBot.Application.RiskManagement.Engine;

public class RiskRuleEngine : IRiskRuleEngine
{
    private readonly ILogger<RiskRuleEngine> _logger;
    private readonly RiskManagementOptions _options;
    private readonly IEnumerable<IRiskRule> _rules;
    private readonly RiskRuleExecutor _executor;
    private readonly IRiskDecisionService _decisionService;
    private readonly RiskCalculationService _calculationService;

    public RiskRuleEngine(
        ILogger<RiskRuleEngine> logger,
        IOptions<RiskManagementOptions> options,
        IEnumerable<IRiskRule> rules,
        RiskRuleExecutor executor,
        IRiskDecisionService decisionService,
        RiskCalculationService calculationService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new RiskManagementException("Critical Error: Missing RiskManagementOptions configuration.");
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _decisionService = decisionService ?? throw new ArgumentNullException(nameof(decisionService));
        _calculationService = calculationService ?? throw new ArgumentNullException(nameof(calculationService));

        if (string.IsNullOrEmpty(_options.DefaultProfile))
        {
            _logger.LogError("Critical Error: Missing configuration options inside RiskManagement.");
            throw new RiskManagementException("Critical Error: Missing configuration options inside RiskManagement.");
        }
    }

    public async Task<RiskEvaluation> EvaluateAsync(TradeRiskContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        // 1. Validate Context
        if (context == null || string.IsNullOrWhiteSpace(context.Symbol) || context.AccountBalance <= 0 || context.EntryPrice <= 0)
        {
            _logger.LogWarning("Invalid Context: Evaluation needs review.");
            stopwatch.Stop();
            return new RiskEvaluation
            {
                SignalId = context?.SignalId ?? Guid.Empty,
                RiskAmount = 0m,
                PositionSize = 0m,
                RiskReward = 0m,
                Exposure = context?.CurrentExposure ?? 0m,
                Decision = RiskDecisionStatus.NeedsReview,
                Reason = "Invalid Context: Missing or corrupt trade details.",
                ExecutedRules = Array.Empty<string>(),
                PassedRules = Array.Empty<string>(),
                FailedRules = Array.Empty<string>(),
                Warnings = new[] { "Context validation failed." },
                Errors = new[] { "Missing or corrupt trade details." },
                ExecutionTime = stopwatch.Elapsed,
                RiskLevel = RiskLevel.Critical
            };
        }

        // Calculate baseline metrics first
        RiskCalculationResult calcResult;
        try
        {
            calcResult = _calculationService.Calculate(context);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid Context: Risk calculation failed for signal {SignalId}", context.SignalId);
            stopwatch.Stop();
            return new RiskEvaluation
            {
                SignalId = context.SignalId,
                RiskAmount = 0m,
                PositionSize = 0m,
                RiskReward = 0m,
                Exposure = context.CurrentExposure,
                Decision = RiskDecisionStatus.NeedsReview,
                Reason = $"Invalid Context: Risk calculation failed. {ex.Message}",
                ExecutedRules = Array.Empty<string>(),
                PassedRules = Array.Empty<string>(),
                FailedRules = Array.Empty<string>(),
                Warnings = new[] { "Calculation failed." },
                Errors = new[] { ex.Message },
                ExecutionTime = stopwatch.Elapsed,
                RiskLevel = RiskLevel.Critical
            };
        }

        // 2. Execute rules sequentially
        var ruleResults = new List<RiskRuleResult>();
        foreach (var rule in _rules)
        {
            var ruleResult = await _executor.ExecuteRuleAsync(rule, context);
            ruleResults.Add(ruleResult);
        }

        // 3. Compile aggregated lists
        var executedRules = ruleResults.Select(r => r.RuleName).ToList();
        var passedRules = ruleResults.Where(r => r.Passed).Select(r => r.RuleName).ToList();
        var failedRules = ruleResults.Where(r => !r.Passed).Select(r => r.RuleName).ToList();

        var warnings = ruleResults
            .Where(r => !r.Passed && r.Severity == RiskRuleSeverity.Warning)
            .Select(r => r.Message)
            .ToList();

        var errors = ruleResults
            .Where(r => !r.Passed && (r.Severity == RiskRuleSeverity.Error || r.Severity == RiskRuleSeverity.Critical))
            .Select(r => r.Message)
            .ToList();

        // Determine RiskLevel
        RiskLevel finalRiskLevel = RiskLevel.Low;
        if (ruleResults.Any(r => !r.Passed && r.Severity == RiskRuleSeverity.Critical))
        {
            finalRiskLevel = RiskLevel.Critical;
        }
        else if (ruleResults.Any(r => !r.Passed && r.Severity == RiskRuleSeverity.Error))
        {
            finalRiskLevel = RiskLevel.High;
        }
        else if (ruleResults.Any(r => !r.Passed && r.Severity == RiskRuleSeverity.Warning))
        {
            finalRiskLevel = RiskLevel.Medium;
        }

        // Get Decision
        var decision = _decisionService.CreateDecision(ruleResults);

        // Map RejectOnCritical override
        var finalDecisionStatus = decision.Decision;
        var finalReason = decision.Reason;

        if (_options.RejectOnCritical && finalRiskLevel == RiskLevel.Critical)
        {
            finalDecisionStatus = RiskDecisionStatus.Rejected;
            finalReason = $"Rejected due to critical risk rules violation: {string.Join("; ", errors)}";
        }

        stopwatch.Stop();

        var evaluation = new RiskEvaluation
        {
            SignalId = context.SignalId,
            RiskAmount = calcResult.RiskAmount,
            PositionSize = calcResult.PositionSize,
            RiskReward = calcResult.RiskReward,
            Exposure = context.CurrentExposure + (calcResult.PositionSize * context.EntryPrice),
            Decision = finalDecisionStatus,
            Reason = finalReason,
            ExecutedRules = executedRules,
            PassedRules = passedRules,
            FailedRules = failedRules,
            Warnings = warnings,
            Errors = errors,
            ExecutionTime = stopwatch.Elapsed,
            RiskLevel = finalRiskLevel
        };

        _logger.LogInformation("Risk Evaluation Completed");

        return evaluation;
    }
}
