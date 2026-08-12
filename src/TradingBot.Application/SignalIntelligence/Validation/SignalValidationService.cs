using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TradingBot.Application.SignalIntelligence.Configuration;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.SignalIntelligence.Enums;
using TradingBot.Domain.Enums;

namespace TradingBot.Application.SignalIntelligence.Validation;

public class SignalValidationService : ISignalValidationService
{
    private readonly SignalIntelligenceOptions _options;

    public SignalValidationService(IOptions<SignalIntelligenceOptions> options)
    {
        _options = options?.Value ?? new SignalIntelligenceOptions();
    }

    public SignalValidationResult Validate(ParsedMessageResult result)
    {
        var validationResult = new SignalValidationResult();

        if (result == null)
        {
            validationResult.IsValid = false;
            validationResult.ValidationStatus = "REJECT";
            validationResult.Errors.Add("ParsedMessageResult is null.");
            return validationResult;
        }

        // 1. Validate Message Classification (Enum validation)
        if (!Enum.IsDefined(typeof(MessageType), result.Type))
        {
            validationResult.Errors.Add($"Invalid message type: {result.Type}");
        }

        // 2. Validate Confidence Score
        if (result.Confidence < _options.MinimumConfidence)
        {
            validationResult.Errors.Add($"Confidence score ({result.Confidence}) is below minimum threshold ({_options.MinimumConfidence})");
        }

        // 3. Signal Data Validation
        if (result.Type == MessageType.SIGNAL)
        {
            // Required fields: Symbol, Side, Entry
            if (string.IsNullOrWhiteSpace(result.Symbol))
            {
                validationResult.Errors.Add("Symbol is required for SIGNAL message type.");
            }

            if (result.Side == null || !Enum.IsDefined(typeof(OrderSide), result.Side.Value))
            {
                validationResult.Errors.Add("Valid Side is required for SIGNAL message type.");
            }

            if (result.Entry == null || result.Entry <= 0)
            {
                validationResult.Errors.Add("Valid Entry price is required for SIGNAL message type.");
            }

            // Check if prices are valid decimals
            if (result.StopLoss.HasValue && result.StopLoss.Value <= 0)
            {
                validationResult.Errors.Add("StopLoss must be greater than zero.");
            }

            if (result.TakeProfits != null)
            {
                foreach (var tp in result.TakeProfits)
                {
                    if (tp <= 0)
                    {
                        validationResult.Errors.Add("Take profit targets must be greater than zero.");
                    }
                }
            }
        }
        // 4. Trade Update Validation
        else if (result.Type == MessageType.TRADE_UPDATE || result.Type == MessageType.CANCEL_COMMAND)
        {
            if (result.Action == null)
            {
                validationResult.Errors.Add("Action is required for TRADE_UPDATE message type.");
            }
            else
            {
                // Allowed actions: MOVE_STOP_TO_ENTRY, UPDATE_STOP_LOSS, UPDATE_TAKE_PROFIT, CLOSE_PARTIAL, CANCEL, CLOSE_POSITION
                var allowedActions = new TradeAction[]
                {
                    TradeAction.MOVE_STOP_TO_ENTRY,
                    TradeAction.UPDATE_STOP_LOSS,
                    TradeAction.UPDATE_TAKE_PROFIT,
                    TradeAction.CLOSE_PARTIAL,
                    TradeAction.CANCEL,
                    TradeAction.CLOSE_POSITION
                };

                if (!allowedActions.Contains(result.Action.Value))
                {
                    validationResult.Errors.Add($"Unknown or invalid action for trade update: {result.Action.Value}");
                }
            }
        }

        // Determine final validation status
        if (validationResult.Errors.Any())
        {
            validationResult.IsValid = false;
            // If confidence is the ONLY issue, let's see if we should flag it as REVIEW_REQUIRED
            bool onlyConfidenceIssue = validationResult.Errors.Count == 1 &&
                                       validationResult.Errors[0].Contains("Confidence score");

            validationResult.ValidationStatus = onlyConfidenceIssue ? "REVIEW_REQUIRED" : "REJECT";
        }
        else
        {
            validationResult.IsValid = true;
            validationResult.ValidationStatus = "ACCEPT";
        }

        return validationResult;
    }

    // 5. AI Response Validation (strictly validated schema & enum checking)
    public SignalValidationResult ValidateAIResponse(string jsonResponse)
    {
        var validationResult = new SignalValidationResult();

        if (string.IsNullOrWhiteSpace(jsonResponse))
        {
            validationResult.IsValid = false;
            validationResult.ValidationStatus = "REJECT";
            validationResult.Errors.Add("AI Response is empty.");
            return validationResult;
        }

        try
        {
            // Parse JSON
            using var document = JsonDocument.Parse(jsonResponse);
            var root = document.RootElement;

            // Check if is object
            if (root.ValueKind != JsonValueKind.Object)
            {
                validationResult.Errors.Add("AI Response must be a JSON object.");
                validationResult.IsValid = false;
                validationResult.ValidationStatus = "REJECT";
                return validationResult;
            }

            // Schema validation: Required field "type"
            if (!root.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
            {
                validationResult.Errors.Add("Missing required field: type.");
            }
            else
            {
                var typeStr = typeProp.GetString();
                if (string.IsNullOrWhiteSpace(typeStr) || !Enum.TryParse<MessageType>(typeStr, out var messageType))
                {
                    validationResult.Errors.Add($"Invalid MessageType: '{typeStr}'");
                }
                else
                {
                    if (messageType == MessageType.SIGNAL)
                    {
                        // Check for symbol, side, entry
                        if (!root.TryGetProperty("symbol", out var symProp) || symProp.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(symProp.GetString()))
                        {
                            validationResult.Errors.Add("Missing required field 'symbol' for SIGNAL.");
                        }
                        if (!root.TryGetProperty("side", out var sideProp) || sideProp.ValueKind != JsonValueKind.String || !Enum.TryParse<OrderSide>(sideProp.GetString(), true, out _))
                        {
                            validationResult.Errors.Add("Missing or invalid field 'side' for SIGNAL.");
                        }
                        if (!root.TryGetProperty("entry", out var entryProp) || (entryProp.ValueKind != JsonValueKind.Number && entryProp.ValueKind != JsonValueKind.String))
                        {
                            validationResult.Errors.Add("Missing or invalid field 'entry' for SIGNAL.");
                        }
                    }
                    else if (messageType == MessageType.TRADE_UPDATE || messageType == MessageType.CANCEL_COMMAND)
                    {
                        if (!root.TryGetProperty("action", out var actionProp) || actionProp.ValueKind != JsonValueKind.String)
                        {
                            validationResult.Errors.Add("Missing or invalid field 'action' for TRADE_UPDATE.");
                        }
                        else
                        {
                            var actStr = actionProp.GetString();
                            // Handle both PARTIAL_CLOSE and CLOSE_PARTIAL
                            if (actStr == "PARTIAL_CLOSE")
                            {
                                actStr = "CLOSE_PARTIAL";
                            }

                            if (string.IsNullOrWhiteSpace(actStr) || !Enum.TryParse<TradeAction>(actStr, true, out _))
                            {
                                validationResult.Errors.Add($"Invalid action: '{actionProp.GetString()}'");
                            }
                        }
                    }
                }
            }

            // Check field "confidence"
            if (root.TryGetProperty("confidence", out var confProp))
            {
                if (confProp.ValueKind != JsonValueKind.Number)
                {
                    validationResult.Errors.Add("Field 'confidence' must be a number.");
                }
            }
        }
        catch (JsonException ex)
        {
            validationResult.Errors.Add($"Invalid JSON syntax: {ex.Message}");
        }

        if (validationResult.Errors.Any())
        {
            validationResult.IsValid = false;
            validationResult.ValidationStatus = "REJECT";
        }
        else
        {
            validationResult.IsValid = true;
            validationResult.ValidationStatus = "ACCEPT";
        }

        return validationResult;
    }
}
