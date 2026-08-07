using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBot.Application.Repositories;
using TradingBot.Application.RiskManagement.Exceptions;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Application.RiskManagement.Models;
using TradingBot.Application.RiskManagement.Services;
using TradingBot.Domain.RiskManagement.Entities;
using TradingBot.Domain.RiskManagement.Enums;
using TradingBot.Domain.RiskManagement.ValueObjects;
using TradingBot.Infrastructure.RiskManagement.Configuration;

namespace TradingBot.Infrastructure.RiskManagement.Services;

public class RiskEngineService : IRiskEngine
{
    private readonly ILogger<RiskEngineService> _logger;
    private readonly RiskManagementOptions _options;
    private readonly IRiskDecisionService _decisionService;
    private readonly IEnumerable<IRiskRule> _rules;
    private readonly RiskCalculationService _calculationService;
    private readonly IRiskEvaluationRepository _riskEvaluationRepository;
    private readonly IUnitOfWork _unitOfWork;

    public RiskEngineService(
        ILogger<RiskEngineService> logger,
        IOptions<RiskManagementOptions> options,
        IRiskDecisionService decisionService,
        IEnumerable<IRiskRule> rules,
        RiskCalculationService calculationService,
        IRiskEvaluationRepository riskEvaluationRepository,
        IUnitOfWork unitOfWork)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _decisionService = decisionService ?? throw new ArgumentNullException(nameof(decisionService));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _calculationService = calculationService ?? throw new ArgumentNullException(nameof(calculationService));
        _riskEvaluationRepository = riskEvaluationRepository ?? throw new ArgumentNullException(nameof(riskEvaluationRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));

        _logger.LogInformation("Risk Engine Initialized");
        _logger.LogInformation("Risk Configuration Loaded");
    }

    public async Task<TradeDecision> EvaluateAsync(TradeRiskContext context)
    {
        _logger.LogInformation("Risk Evaluation Started");

        if (!_options.Enabled)
        {
            return new TradeDecision
            {
                Decision = RiskDecisionStatus.Approved,
                Reason = "Risk management is disabled in configuration.",
                CreatedAt = DateTime.UtcNow
            };
        }

        RiskEvaluation evaluation;
        TradeDecision finalDecision;

        try
        {
            // 1. Calculate Risk Metrics
            var calcResult = _calculationService.Calculate(context);

            // 2. Evaluate existing rules (for future stages / backward compatibility)
            var results = new List<RiskRuleResult>();
            foreach (var rule in _rules)
            {
                var result = await rule.EvaluateAsync(context);
                results.Add(result);
            }

            if (results.Count == 0)
            {
                finalDecision = new TradeDecision
                {
                    Decision = RiskDecisionStatus.Approved,
                    Reason = "Risk calculation completed successfully.",
                    CreatedAt = DateTime.UtcNow
                };
            }
            else
            {
                finalDecision = _decisionService.CreateDecision(results);
            }

            // 3. Map calculation results and decision to RiskEvaluation
            evaluation = new RiskEvaluation
            {
                SignalId = context.SignalId,
                RiskAmount = calcResult.RiskAmount,
                PositionSize = calcResult.PositionSize,
                RiskReward = calcResult.RiskReward,
                Exposure = context.CurrentExposure + (calcResult.PositionSize * context.EntryPrice),
                Decision = finalDecision.Decision,
                Reason = finalDecision.Reason
            };
        }
        catch (RiskManagementException ex)
        {
            _logger.LogWarning(ex, "Risk calculation failed for signal {SignalId}", context.SignalId);

            finalDecision = new TradeDecision
            {
                Decision = RiskDecisionStatus.Rejected,
                Reason = $"Risk calculation failed: {ex.Message}",
                CreatedAt = DateTime.UtcNow
            };

            evaluation = new RiskEvaluation
            {
                SignalId = context.SignalId,
                RiskAmount = 0m,
                PositionSize = 0m,
                RiskReward = 0m,
                Exposure = context.CurrentExposure,
                Decision = finalDecision.Decision,
                Reason = finalDecision.Reason
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during risk calculation for signal {SignalId}", context.SignalId);

            finalDecision = new TradeDecision
            {
                Decision = RiskDecisionStatus.Rejected,
                Reason = $"Risk calculation failed with an unexpected error: {ex.Message}",
                CreatedAt = DateTime.UtcNow
            };

            evaluation = new RiskEvaluation
            {
                SignalId = context.SignalId,
                RiskAmount = 0m,
                PositionSize = 0m,
                RiskReward = 0m,
                Exposure = context.CurrentExposure,
                Decision = finalDecision.Decision,
                Reason = finalDecision.Reason
            };
        }

        // 4. Persist RiskEvaluation to Database
        await _riskEvaluationRepository.AddAsync(evaluation);
        await _unitOfWork.SaveChangesAsync();

        // 5. Return final decision
        return finalDecision;
    }
}
