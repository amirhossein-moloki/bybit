using TradingBot.Domain.RiskManagement.Enums;

namespace TradingBot.Application.RiskManagement.Models;

public record RiskRuleResult
{
    public string RuleName { get; init; } = string.Empty;
    public bool Passed { get; init; }
    public RiskRuleSeverity Severity { get; init; }
    public string Message { get; init; } = string.Empty;
}
