using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.SignalIntelligence.Enums;
using TradingBot.Domain.SignalIntelligence.Models;
using TradingBot.Parser.Interfaces;

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
            _logger.LogError(ex, "AI Intent Interpreter failed for MessageId {MessageId}. Returning UNKNOWN intent fallback.",
                context.CurrentMessage.MessageId);

            return new StructuredIntent
            {
                MessageType = IntelligenceMessageType.UNKNOWN,
                Intent = TradingIntent.UNKNOWN,
                Scope = IntentScope.NONE,
                Targets = new List<string>(),
                Confidence = 0.0m,
                RequiresExecution = false,
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

        // Clean markdown codeblocks if AI wraps JSON in ```json ... ```
        var cleanJson = jsonResponse.Trim();
        if (cleanJson.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            cleanJson = cleanJson.Substring(7);
        }
        if (cleanJson.StartsWith("```"))
        {
            cleanJson = cleanJson.Substring(3);
        }
        if (cleanJson.EndsWith("```"))
        {
            cleanJson = cleanJson.Substring(0, cleanJson.Length - 3);
        }
        cleanJson = cleanJson.Trim();

        using var doc = JsonDocument.Parse(cleanJson);
        var root = doc.RootElement;

        var messageTypeStr = root.TryGetProperty("messageType", out var mtProp) ? mtProp.GetString() : "UNKNOWN";
        var intentStr = root.TryGetProperty("intent", out var inProp) ? inProp.GetString() : "UNKNOWN";
        var scopeStr = root.TryGetProperty("scope", out var scProp) ? scProp.GetString() : "NONE";
        var confidence = root.TryGetProperty("confidence", out var cfProp) ? cfProp.GetDecimal() : 0.0m;
        var requiresExecution = root.TryGetProperty("requiresExecution", out var reProp) && reProp.GetBoolean();
        var reason = root.TryGetProperty("reason", out var rsProp) ? rsProp.GetString() ?? "" : "";

        var targets = new List<string>();
        if (root.TryGetProperty("targets", out var tgProp) && tgProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var elem in tgProp.EnumerateArray())
            {
                var t = elem.GetString();
                if (!string.IsNullOrWhiteSpace(t)) targets.Add(t.Trim().ToUpperInvariant());
            }
        }

        Enum.TryParse<IntelligenceMessageType>(messageTypeStr, true, out var messageType);
        Enum.TryParse<TradingIntent>(intentStr, true, out var intent);
        Enum.TryParse<IntentScope>(scopeStr, true, out var scope);

        return new StructuredIntent
        {
            MessageType = messageType,
            Intent = intent,
            Scope = scope,
            Targets = targets,
            Confidence = Math.Clamp(confidence, 0.0m, 1.0m),
            RequiresExecution = requiresExecution,
            Reason = reason,
            DetectionMethod = "AI"
        };
    }
}
