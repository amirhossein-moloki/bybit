using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TradingBot.Application.Repositories;
using TradingBot.Application.RiskManagement.Interfaces;
using TradingBot.Application.RiskManagement.Models;
using TradingBot.Domain.RiskManagement.ValueObjects;
using TradingBot.Domain.RiskManagement.Enums;
using TradingBot.Domain.Enums;
using TradingBot.Application.RiskManagement.Configuration;

namespace TradingBot.Application.RiskManagement.Rules;

public class DuplicatePositionRule : IRiskRule
{
    private readonly RiskManagementOptions _options;
    private readonly IPositionRepository _positionRepository;

    public DuplicatePositionRule(
        IOptions<RiskManagementOptions> options,
        IPositionRepository positionRepository)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
    }

    public async Task<RiskRuleResult> EvaluateAsync(TradeRiskContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (!_options.OnePositionPerSymbol)
        {
            return new RiskRuleResult
            {
                RuleName = nameof(DuplicatePositionRule),
                Passed = true,
                Severity = RiskRuleSeverity.Info,
                Message = "One position per symbol limit is disabled."
            };
        }

        var openPositions = await _positionRepository.GetOpenPositionsAsync();
        bool hasDuplicate = openPositions.Any(p =>
            p.Symbol.Equals(context.Symbol, StringComparison.OrdinalIgnoreCase) &&
            p.Status == PositionStatus.Open);

        return new RiskRuleResult
        {
            RuleName = nameof(DuplicatePositionRule),
            Passed = !hasDuplicate,
            Severity = RiskRuleSeverity.Error,
            Message = !hasDuplicate
                ? $"No existing open position found for symbol {context.Symbol}."
                : $"Rejected. An open position already exists for symbol {context.Symbol}."
        };
    }
}
