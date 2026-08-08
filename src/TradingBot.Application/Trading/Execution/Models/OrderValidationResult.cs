using System.Collections.Generic;
using TradingBot.Application.Trading.Execution.Enums;

namespace TradingBot.Application.Trading.Execution.Models;

public class OrderValidationResult
{
    public bool IsValid { get; set; } = true;
    public ValidationSeverity Severity { get; set; } = ValidationSeverity.Info;
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> ValidationCodes { get; set; } = new();

    public void AddError(string error, string code, ValidationSeverity severity = ValidationSeverity.Error)
    {
        IsValid = false;
        Errors.Add(error);
        ValidationCodes.Add(code);
        if (severity > Severity)
        {
            Severity = severity;
        }
    }

    public void AddWarning(string warning, string code)
    {
        Warnings.Add(warning);
        ValidationCodes.Add(code);
        if (Severity < ValidationSeverity.Warning)
        {
            Severity = ValidationSeverity.Warning;
        }
    }
}
