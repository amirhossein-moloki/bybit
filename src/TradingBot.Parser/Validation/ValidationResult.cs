using System;
using System.Collections.Generic;

namespace TradingBot.Parser.Validation;

public class ValidationResult
{
    public bool IsValid { get; set; } = true;
    public string ValidationStatus { get; set; } = "Validated"; // "Validated", "Rejected", "RequiresReview"
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> FailedRules { get; } = new();
    public DateTime ValidatedAt { get; set; } = DateTime.UtcNow;
}
