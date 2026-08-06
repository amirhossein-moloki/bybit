using System;
using System.Linq;
using System.Threading.Tasks;
using TradingBot.Domain.Enums;

namespace TradingBot.Parser.Validation.Rules;

public class BusinessConsistencyValidationRule : IValidationRule
{
    public Task ValidateAsync(ValidationContext context, ValidationResult result)
    {
        var signal = context.ParsedSignal;
        var side = signal.Side;
        var entry = signal.EntryPrice;
        var sl = signal.StopLoss;
        var tps = signal.TakeProfits;

        if (side == null || entry == null || entry <= 0)
        {
            // Cannot perform consistency checks if direction or entry price is invalid/missing.
            // These will be caught by their respective rules.
            return Task.CompletedTask;
        }

        if (side == OrderSide.Buy) // LONG
        {
            if (sl != null && sl >= entry)
            {
                result.IsValid = false;
                result.ValidationStatus = "Rejected";
                result.Errors.Add($"For LONG trade, Stop Loss ({sl}) must be less than Entry Price ({entry}).");
                result.FailedRules.Add(nameof(BusinessConsistencyValidationRule));
            }

            if (tps != null && tps.Any())
            {
                foreach (var tp in tps)
                {
                    if (tp <= entry)
                    {
                        result.IsValid = false;
                        result.ValidationStatus = "Rejected";
                        result.Errors.Add($"For LONG trade, Take Profit ({tp}) must be greater than Entry Price ({entry}).");
                        result.FailedRules.Add(nameof(BusinessConsistencyValidationRule));
                    }
                }
            }
        }
        else if (side == OrderSide.Sell) // SHORT
        {
            if (sl != null && sl <= entry)
            {
                result.IsValid = false;
                result.ValidationStatus = "Rejected";
                result.Errors.Add($"For SHORT trade, Stop Loss ({sl}) must be greater than Entry Price ({entry}).");
                result.FailedRules.Add(nameof(BusinessConsistencyValidationRule));
            }

            if (tps != null && tps.Any())
            {
                foreach (var tp in tps)
                {
                    if (tp >= entry)
                    {
                        result.IsValid = false;
                        result.ValidationStatus = "Rejected";
                        result.Errors.Add($"For SHORT trade, Take Profit ({tp}) must be less than Entry Price ({entry}).");
                        result.FailedRules.Add(nameof(BusinessConsistencyValidationRule));
                    }
                }
            }
        }

        return Task.CompletedTask;
    }
}
