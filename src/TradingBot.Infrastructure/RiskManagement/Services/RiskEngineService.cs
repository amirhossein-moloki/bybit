using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBot.Application.Repositories;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Application.RiskManagement.Services;
using TradingBot.Domain.RiskManagement.Entities;
using TradingBot.Domain.RiskManagement.Enums;
using TradingBot.Domain.RiskManagement.ValueObjects;
using TradingBot.Application.RiskManagement.Configuration;

namespace TradingBot.Infrastructure.RiskManagement.Services;

public class RiskEngineService : IRiskEngine
{
    private readonly ILogger<RiskEngineService> _logger;
    private readonly RiskManagementOptions _options;
    private readonly IRiskRuleEngine _ruleEngine;
    private readonly IRiskEvaluationRepository _riskEvaluationRepository;
    private readonly IUnitOfWork _unitOfWork;

    public RiskEngineService(
        ILogger<RiskEngineService> logger,
        IOptions<RiskManagementOptions> options,
        IRiskRuleEngine ruleEngine,
        IRiskEvaluationRepository riskEvaluationRepository,
        IUnitOfWork unitOfWork)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _ruleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
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

        try
        {
            evaluation = await _ruleEngine.EvaluateAsync(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during risk calculation for signal {SignalId}", context.SignalId);

            evaluation = new RiskEvaluation
            {
                SignalId = context.SignalId,
                RiskAmount = 0m,
                PositionSize = 0m,
                RiskReward = 0m,
                Exposure = context.CurrentExposure,
                Decision = RiskDecisionStatus.Rejected,
                Reason = $"Risk calculation failed with an unexpected error: {ex.Message}"
            };
        }

        // 4. Persist RiskEvaluation to Database
        await _riskEvaluationRepository.AddAsync(evaluation);
        await _unitOfWork.SaveChangesAsync();

        // 5. Return final decision
        return new TradeDecision
        {
            Decision = evaluation.Decision,
            Reason = evaluation.Reason,
            CreatedAt = DateTime.UtcNow
        };
    }
}
