using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TradingBot.Parser.Configuration;

namespace TradingBot.Parser.Validation.Rules;

public class LeverageValidationRule : IValidationRule
{
    private readonly IOptions<ValidationOptions> _options;

    public LeverageValidationRule(IOptions<ValidationOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Task ValidateAsync(ValidationContext context, ValidationResult result)
    {
        var leverage = context.ParsedSignal.Leverage;
        if (leverage != null)
        {
            if (leverage <= 0)
            {
                result.IsValid = false;
                result.ValidationStatus = "Rejected";
                result.Errors.Add($"Leverage must be positive. Found: {leverage}.");
                result.FailedRules.Add(nameof(LeverageValidationRule));
            }
            else if (leverage > _options.Value.MaximumLeverage)
            {
                result.IsValid = false;
                result.ValidationStatus = "Rejected";
                result.Errors.Add($"Leverage ({leverage}) exceeds maximum configured limit of {_options.Value.MaximumLeverage}.");
                result.FailedRules.Add(nameof(LeverageValidationRule));
            }
        }

        return Task.CompletedTask;
    }
}
