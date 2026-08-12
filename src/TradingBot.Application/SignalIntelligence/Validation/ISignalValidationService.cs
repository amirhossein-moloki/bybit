using TradingBot.Application.SignalIntelligence.Contracts;

namespace TradingBot.Application.SignalIntelligence.Validation;

public interface ISignalValidationService
{
    SignalValidationResult Validate(ParsedMessageResult result);
    SignalValidationResult ValidateAIResponse(string jsonResponse);
}
