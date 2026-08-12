using System.Collections.Generic;

namespace TradingBot.Application.SignalIntelligence.Validation;

public class SignalValidationResult
{
    public bool IsValid { get; set; }
    public string? ValidationStatus { get; set; } // ACCEPT, REVIEW_REQUIRED, REJECT
    public List<string> Errors { get; set; } = new();
}
