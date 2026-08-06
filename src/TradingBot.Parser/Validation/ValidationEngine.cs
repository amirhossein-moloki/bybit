using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;
using TradingBot.Parser.Models;

namespace TradingBot.Parser.Validation;

public class ValidationEngine : ISignalValidator
{
    private readonly IEnumerable<IValidationRule> _rules;
    private readonly ISignalRepository _signalRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ValidationEngine> _logger;

    public ValidationEngine(
        IEnumerable<IValidationRule> rules,
        ISignalRepository signalRepository,
        IUnitOfWork unitOfWork,
        ILogger<ValidationEngine> logger)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _signalRepository = signalRepository ?? throw new ArgumentNullException(nameof(signalRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ValidationResult> ValidateAsync(
        Signal signal,
        ParsedSignal parsedSignal,
        string sourceChannel = "UNKNOWN",
        string templateName = "Default",
        string parserVersion = "1.0"
    )
    {
        _logger.LogInformation("Validation Started for SignalId: {SignalId}", signal.Id);

        var result = new ValidationResult();

        if (parsedSignal == null)
        {
            _logger.LogError("Validation Failed: ParsedSignal is null.");
            result.IsValid = false;
            result.ValidationStatus = "Rejected";
            result.Errors.Add("ParsedSignal is null.");

            try
            {
                signal.MarkRejected();
                signal.SetValidationResult("Rejected", "ParsedSignal was null.", parserVersion);
                _signalRepository.Update(signal);
                await _unitOfWork.SaveChangesAsync();
                _logger.LogInformation("Signal Rejected for SignalId: {SignalId}", signal.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error saving rejection for SignalId: {SignalId}", signal.Id);
            }

            return result;
        }

        var context = new ValidationContext(signal.Id, parsedSignal, sourceChannel, templateName, parserVersion);

        // Execute rules independently
        foreach (var rule in _rules)
        {
            var ruleName = rule.GetType().Name;
            try
            {
                await rule.ValidateAsync(context, result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Validation Exception in rule {RuleName} for SignalId: {SignalId}", ruleName, signal.Id);
                result.Errors.Add($"Rule {ruleName} failed with an unhandled exception: {ex.Message}");
                result.IsValid = false;
                result.FailedRules.Add(ruleName);
            }
        }

        // Aggregate results
        if (result.Errors.Any())
        {
            result.IsValid = false;
            result.ValidationStatus = "Rejected";
        }

        try
        {
            // Update signal properties with parsed details if we successfully parsed and validated, or even if rejected/requires review
            if (parsedSignal.Symbol != null && parsedSignal.EntryPrice.HasValue && parsedSignal.Side.HasValue)
            {
                signal.UpdateParsedDetails(
                    parsedSignal.Symbol,
                    parsedSignal.Side.Value,
                    parsedSignal.EntryPrice.Value,
                    parsedSignal.StopLoss,
                    parsedSignal.TakeProfits?.FirstOrDefault(),
                    parsedSignal.Leverage
                );
            }

            // Update signal status based on validation result
            if (result.IsValid)
            {
                result.ValidationStatus = "Validated";
                signal.MarkValidated();
                signal.MarkReadyForRiskEngine();
                _logger.LogInformation("Validation Passed for SignalId: {SignalId}", signal.Id);
                _logger.LogInformation("Signal Ready For Risk Engine: {SignalId}", signal.Id);
            }
            else
            {
                result.ValidationStatus = "Rejected";
                signal.MarkRejected();
                _logger.LogWarning("Validation Failed for SignalId: {SignalId}", signal.Id);
                _logger.LogWarning("Signal Rejected: {SignalId}", signal.Id);
            }

            var validationMessage = string.Join("; ", result.Errors.Concat(result.Warnings));
            signal.SetValidationResult(result.ValidationStatus, validationMessage, parserVersion);

            _signalRepository.Update(signal);
            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected Error processing validation persist/status update for SignalId: {SignalId}", signal.Id);

            try
            {
                result.ValidationStatus = "RequiresReview";
                result.IsValid = false;
                signal.SetValidationResult("RequiresReview", $"Unexpected validation error: {ex.Message}", parserVersion);
                _signalRepository.Update(signal);
                await _unitOfWork.SaveChangesAsync();
            }
            catch (Exception innerEx)
            {
                _logger.LogError(innerEx, "Failed to update Signal to RequiresReview for SignalId: {SignalId}", signal.Id);
            }
        }

        return result;
    }
}
