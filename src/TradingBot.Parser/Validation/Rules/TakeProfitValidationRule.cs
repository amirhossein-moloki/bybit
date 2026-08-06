using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TradingBot.Parser.Configuration;

namespace TradingBot.Parser.Validation.Rules;

public class TakeProfitValidationRule : IValidationRule
{
    private readonly IOptions<ValidationOptions> _options;

    public TakeProfitValidationRule(IOptions<ValidationOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Task ValidateAsync(ValidationContext context, ValidationResult result)
    {
        var takeProfits = context.ParsedSignal.TakeProfits;
        var requireTakeProfit = _options.Value.RequireTakeProfit;

        if (takeProfits == null || !takeProfits.Any())
        {
            if (requireTakeProfit)
            {
                result.IsValid = false;
                result.ValidationStatus = "Rejected";
                result.Errors.Add("Take profit targets are required but missing.");
                result.FailedRules.Add(nameof(TakeProfitValidationRule));
            }
        }
        else
        {
            foreach (var tp in takeProfits)
            {
                if (tp <= 0)
                {
                    result.IsValid = false;
                    result.ValidationStatus = "Rejected";
                    result.Errors.Add($"Take profit target must be positive. Found: {tp}.");
                    result.FailedRules.Add(nameof(TakeProfitValidationRule));
                }
            }
        }

        return Task.CompletedTask;
    }
}
