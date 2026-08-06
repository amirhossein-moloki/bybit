using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Application.RiskManagement.Models;
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

    public RiskEngineService(
        ILogger<RiskEngineService> logger,
        IOptions<RiskManagementOptions> options,
        IRiskDecisionService decisionService,
        IEnumerable<IRiskRule> rules)
    {
        _logger = logger;
        _options = options.Value;
        _decisionService = decisionService;
        _rules = rules;

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

        var results = new List<RiskRuleResult>();
        foreach (var rule in _rules)
        {
            var result = await rule.EvaluateAsync(context);
            results.Add(result);
        }

        if (results.Count == 0)
        {
            return new TradeDecision
            {
                Decision = RiskDecisionStatus.Approved,
                Reason = "Risk Engine execution succeeded with no rules.",
                CreatedAt = DateTime.UtcNow
            };
        }

        return _decisionService.CreateDecision(results);
    }
}
