using System.Threading.Tasks;

namespace TradingBot.Parser.Validation;

public interface IValidationRule
{
    Task ValidateAsync(
        ValidationContext context,
        ValidationResult result
    );
}
