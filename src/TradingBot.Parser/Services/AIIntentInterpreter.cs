using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.SignalIntelligence.Enums;
using TradingBot.Domain.SignalIntelligence.Models;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;

namespace TradingBot.Parser.Services;

public class AIIntentInterpreter : IAIIntentInterpreter
{
    private readonly IAIProvider _aiProvider;
    private readonly IPromptTemplateEngine _promptTemplateEngine;
    private readonly ILogger<AIIntentInterpreter> _logger;

    public AIIntentInterpreter(
        IAIProvider aiProvider,
        IPromptTemplateEngine promptTemplateEngine,
        ILogger<AIIntentInterpreter> logger)
    {
        _aiProvider = aiProvider ?? throw new ArgumentNullException(nameof(aiProvider));
        _promptTemplateEngine = promptTemplateEngine ?? throw new ArgumentNullException(nameof(promptTemplateEngine));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<StructuredIntent> InterpretAsync(
        MessageIntelligenceContext context,
        CancellationToken cancellationToken = default)
    {
        if (context == null || context.CurrentMessage == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        _logger.LogInformation("AI Intent Interpretation started for MessageId {MessageId} from ChannelId {ChannelId}",
            context.CurrentMessage.MessageId, context.CurrentMessage.ChannelId);

        var contextJson = JsonSerializer.Serialize(context, new JsonSerializerOptions { WriteIndented = false });
        var prompt = _promptTemplateEngine.RenderPrompt("intent_v1", context.CurrentMessage.Text, contextJson);

        try
        {
            var rawAiResponse = await _aiProvider.AnalyzeAsync(prompt, cancellationToken);
            _logger.LogDebug("AI Intent Interpretation response received for MessageId {MessageId}: {Response}",
                context.CurrentMessage.MessageId, rawAiResponse);

            var intent = ParseAndValidateAiResponse(rawAiResponse);
            intent.DetectionMethod = "AI";
            return intent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI Intent Interpreter failed for MessageId {MessageId}. Returning UNKNOWN fallback.",
                context.CurrentMessage.MessageId);

            return new StructuredIntent
            {
                MessageType = IntelligenceMessageType.UNKNOWN,
                Intent = TradingIntent.UNKNOWN,
                Scope = IntentScope.NONE,
                Targets = new List<string>(),
                Confidence = 0.0m,
                RequiresExecution = false,
                ResolutionStatus = ResolutionStatus.Invalid,
                ExecutionMode = ExecutionMode.NoAction,
                IsTradeRelated = false,
                IsExecutableCandidate = false,
                Reason = $"AI interpretation failed or unavailable: {ex.Message}",
                DetectionMethod = "AI"
            };
        }
    }

    private StructuredIntent ParseAndValidateAiResponse(string jsonResponse)
    {
        if (string.IsNullOrWhiteSpace(jsonResponse))
        {
            throw new FormatException("AI response was empty.");
        }

        var cleanJson = CleanJsonResponse(jsonResponse);

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true
        };

        AIUnderstandingResult? dto;
        try
        {
            dto = JsonSerializer.Deserialize<AIUnderstandingResult>(cleanJson, options);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"AI response returned invalid JSON: {ex.Message}", ex);
        }

        if (dto == null)
        {
            throw new FormatException("AI response deserialized to null.");
        }

        // Try extracting messageType, intent, scope from root element if present
        string? msgTypeStr = null;
        string? scopeStr = null;
        using (var doc = JsonDocument.Parse(cleanJson))
        {
            if (doc.RootElement.TryGetProperty("messageType", out var mtProp))
            {
                msgTypeStr = mtProp.GetString();
            }
            if (doc.RootElement.TryGetProperty("scope", out var scProp))
            {
                scopeStr = scProp.GetString();
            }
        }

        var intentStr = dto.Intent ?? dto.Type ?? "Unknown";
        var actionStr = dto.Action ?? "None";
        var resStatusStr = dto.ResolutionStatus ?? "NotTradingRelated";
        var execModeStr = dto.ExecutionMode ?? "NoAction";

        Enum.TryParse<ResolutionStatus>(resStatusStr, true, out var resolutionStatus);
        Enum.TryParse<ExecutionMode>(execModeStr, true, out var executionMode);

        IntelligenceMessageType messageType;
        if (!string.IsNullOrEmpty(msgTypeStr) && Enum.TryParse<IntelligenceMessageType>(msgTypeStr, true, out var parsedType))
        {
            messageType = parsedType;
        }
        else
        {
            messageType = MapIntentToMessageType(intentStr, actionStr);
        }

        var tradingIntent = MapIntentToTradingIntent(intentStr, actionStr);

        var targets = new List<string>();
        if (!string.IsNullOrWhiteSpace(dto.Symbol))
        {
            targets.Add(dto.Symbol.Trim().ToUpperInvariant());
        }

        using (var doc = JsonDocument.Parse(cleanJson))
        {
            if (doc.RootElement.TryGetProperty("targets", out var tgProp) && tgProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var elem in tgProp.EnumerateArray())
                {
                    var t = elem.GetString();
                    if (!string.IsNullOrWhiteSpace(t) && !targets.Contains(t.Trim().ToUpperInvariant(), StringComparer.OrdinalIgnoreCase))
                    {
                        targets.Add(t.Trim().ToUpperInvariant());
                    }
                }
            }
        }

        IntentScope scope;
        if (!string.IsNullOrEmpty(scopeStr) && Enum.TryParse<IntentScope>(scopeStr, true, out var parsedScope))
        {
            scope = parsedScope;
        }
        else
        {
            scope = MapIntentToScope(intentStr, actionStr, dto.Symbol ?? targets.FirstOrDefault());
        }

        bool isExecutableCandidate = dto.IsExecutableCandidate;
        using (var doc = JsonDocument.Parse(cleanJson))
        {
            if (doc.RootElement.TryGetProperty("requiresExecution", out var reProp) && reProp.GetBoolean())
            {
                isExecutableCandidate = true;
                if (dto.ExecutionMode == "NoAction") executionMode = ExecutionMode.Immediate;
                if (dto.ResolutionStatus == "NotTradingRelated") resolutionStatus = ResolutionStatus.Resolved;
            }
        }

        bool requiresExecution = isExecutableCandidate &&
                                  executionMode == ExecutionMode.Immediate &&
                                  resolutionStatus == ResolutionStatus.Resolved;

        return new StructuredIntent
        {
            MessageType = messageType,
            Intent = tradingIntent,
            Scope = scope,
            Targets = targets,
            Confidence = Math.Clamp(dto.Confidence, 0.0m, 1.0m),
            RequiresExecution = requiresExecution,
            Reason = dto.Reason ?? string.Empty,
            DetectionMethod = "AI",

            ResolutionStatus = resolutionStatus,
            ExecutionMode = executionMode,
            IsTradeRelated = dto.IsTradeRelated || isExecutableCandidate,
            IsExecutableCandidate = isExecutableCandidate,
            Action = actionStr,
            Symbol = dto.Symbol ?? targets.FirstOrDefault(),
            PositionReference = dto.PositionReference,
            Side = dto.Side,
            EntryPrice = dto.EntryPrice ?? dto.Entry,
            StopLoss = dto.StopLoss,
            TakeProfit1 = dto.TakeProfit1,
            TakeProfit2 = dto.TakeProfit2,
            TakeProfit3 = dto.TakeProfit3,
            Conditions = dto.Conditions ?? new List<string>(),
            Evidence = dto.Evidence ?? new List<string>()
        };
    }

    private static string CleanJsonResponse(string raw)
    {
        var clean = raw.Trim();
        if (clean.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring(7);
        }
        else if (clean.StartsWith("```"))
        {
            clean = clean.Substring(3);
        }

        if (clean.EndsWith("```"))
        {
            clean = clean.Substring(0, clean.Length - 3);
        }

        return clean.Trim();
    }

    private static IntelligenceMessageType MapIntentToMessageType(string intent, string action)
    {
        var upperIntent = intent.ToUpperInvariant();
        var upperAction = action.ToUpperInvariant();

        if (upperIntent.Contains("ENTRY") || (upperIntent.Contains("SIGNAL") && !upperIntent.Contains("UPDATE") && !upperIntent.Contains("EXIT")))
            return IntelligenceMessageType.NEW_SIGNAL;

        if (upperIntent.Contains("COMMAND") || upperIntent.Contains("POSITIONMANAGEMENT") || upperIntent.Contains("EXITSIGNAL") ||
            upperIntent.Contains("CONDITIONALPOSITIONMANAGEMENT") || upperIntent.Contains("RISK_FREE") || upperIntent.Contains("CLOSE") ||
            upperAction.Contains("CLOSE") || upperAction.Contains("RISK") || upperAction.Contains("CANCEL"))
            return IntelligenceMessageType.COMMAND;

        if (upperIntent.Contains("MARKETANALYSIS") || upperIntent.Contains("ANALYSIS"))
            return IntelligenceMessageType.COMMENTARY;

        if (upperIntent.Contains("INFORMATIONAL") || upperIntent.Contains("STATUS"))
            return IntelligenceMessageType.STATUS;

        return IntelligenceMessageType.UNKNOWN;
    }

    private static TradingIntent MapIntentToTradingIntent(string intent, string action)
    {
        var upperIntent = intent.ToUpperInvariant();
        var upperAction = action.ToUpperInvariant();

        if (upperIntent.Contains("RISK_FREE") || upperAction.Contains("MOVESTOPLOSSTOENTRY") || upperAction.Contains("RISK_FREE") || upperAction.Contains("BREAKEVEN"))
            return TradingIntent.RISK_FREE;

        if (upperIntent.Contains("CLOSE") || upperAction.Contains("CLOSEPOSITION") || upperAction.Contains("EXIT"))
            return TradingIntent.CLOSE;

        if (upperAction.Contains("CLOSEALL"))
            return TradingIntent.CLOSE_ALL;

        if (upperAction.Contains("CANCEL"))
            return TradingIntent.CANCEL_PENDING_ORDERS;

        if (upperAction.Contains("PARTIALCLOSE") || upperAction.Contains("MODIFY"))
            return TradingIntent.SIGNAL_UPDATE;

        if (upperIntent.Contains("CONDITIONAL"))
            return TradingIntent.SIGNAL_UPDATE;

        if (upperIntent.Contains("MARKETANALYSIS") || upperIntent.Contains("INFORMATIONAL") || upperIntent.Contains("NOISE"))
            return TradingIntent.NO_ACTION;

        return TradingIntent.UNKNOWN;
    }

    private static IntentScope MapIntentToScope(string intent, string action, string? symbol)
    {
        if (!string.IsNullOrWhiteSpace(symbol)) return IntentScope.SYMBOL;
        if (action.ToUpperInvariant().Contains("ALL") || intent.ToUpperInvariant().Contains("ALL")) return IntentScope.ALL_ACTIVE_TRADES;
        return IntentScope.SIGNAL;
    }
}
