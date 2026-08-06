using System.Threading.Tasks;

namespace TradingBot.Parser.Validation.Rules;

public class DirectionValidationRule : IValidationRule
{
    public Task ValidateAsync(ValidationContext context, ValidationResult result)
    {
        var side = context.ParsedSignal.Side;
        if (side == null)
        {
            result.IsValid = false;
            result.ValidationStatus = "Rejected";
            result.Errors.Add("Direction (LONG/SHORT) is missing or invalid.");
            result.FailedRules.Add(nameof(DirectionValidationRule));
        }

        return Task.CompletedTask;
    }
}
