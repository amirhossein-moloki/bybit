using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using TradingBot.Parser.Configuration;

namespace TradingBot.Parser.Validation.Rules;

public class StopLossValidationRule : IValidationRule
{
    private readonly IOptions<ValidationOptions> _options;

    public StopLossValidationRule(IOptions<ValidationOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public Task ValidateAsync(ValidationContext context, ValidationResult result)
    {
        var stopLoss = context.ParsedSignal.StopLoss;
        var requireStopLoss = _options.Value.RequireStopLoss;

        if (stopLoss == null)
        {
            if (requireStopLoss)
            {
                result.IsValid = false;
                result.ValidationStatus = "Rejected";
                result.Errors.Add("Stop loss is required but missing.");
                result.FailedRules.Add(nameof(StopLossValidationRule));
            }
        }
        else if (stopLoss <= 0)
        {
            result.IsValid = false;
            result.ValidationStatus = "Rejected";
            result.Errors.Add($"Stop loss must be positive. Found: {stopLoss}.");
            result.FailedRules.Add(nameof(StopLossValidationRule));
        }

        return Task.CompletedTask;
    }
}
