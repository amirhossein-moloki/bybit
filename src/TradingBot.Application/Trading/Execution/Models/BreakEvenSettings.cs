using TradingBot.Domain.Enums;

namespace TradingBot.Application.Trading.Execution.Models;

public class BreakEvenSettings
{
    public bool Enabled { get; set; } = true;
    public BreakEvenTriggerType TriggerType { get; set; } = BreakEvenTriggerType.Percentage;
    public decimal TriggerValue { get; set; } = 1.0m;
    public decimal Offset { get; set; } = 0m;
}
