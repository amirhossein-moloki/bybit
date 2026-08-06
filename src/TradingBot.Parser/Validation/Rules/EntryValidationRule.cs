using System.Threading.Tasks;

namespace TradingBot.Parser.Validation.Rules;

public class EntryValidationRule : IValidationRule
{
    public Task ValidateAsync(ValidationContext context, ValidationResult result)
    {
        var entryPrice = context.ParsedSignal.EntryPrice;
        if (entryPrice == null)
        {
            result.IsValid = false;
            result.ValidationStatus = "Rejected";
            result.Errors.Add("Entry price is missing.");
            result.FailedRules.Add(nameof(EntryValidationRule));
        }
        else if (entryPrice <= 0)
        {
            result.IsValid = false;
            result.ValidationStatus = "Rejected";
            result.Errors.Add($"Entry price must be positive. Found: {entryPrice}.");
            result.FailedRules.Add(nameof(EntryValidationRule));
        }

        return Task.CompletedTask;
    }
}
