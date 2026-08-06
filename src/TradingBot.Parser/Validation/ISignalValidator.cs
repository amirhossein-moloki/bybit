using System.Threading.Tasks;
using TradingBot.Domain.Entities;
using TradingBot.Parser.Models;

namespace TradingBot.Parser.Validation;

public interface ISignalValidator
{
    Task<ValidationResult> ValidateAsync(
        Signal signal,
        ParsedSignal parsedSignal,
        string sourceChannel = "UNKNOWN",
        string templateName = "Default",
        string parserVersion = "1.0"
    );
}
