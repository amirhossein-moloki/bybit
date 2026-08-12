using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Domain.SignalIntelligence.Events;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;

namespace TradingBot.Parser.Services;

public class AIAnalyzer : IAIAnalyzer
{
    private readonly IAIProvider _aiProvider;
    private readonly IPromptTemplateEngine _promptTemplateEngine;
    private readonly IIntelligenceEventPublisher _eventPublisher;
    private readonly ILogger<AIAnalyzer> _logger;

    public AIAnalyzer(
        IAIProvider aiProvider,
        IPromptTemplateEngine promptTemplateEngine,
        IIntelligenceEventPublisher eventPublisher,
        ILogger<AIAnalyzer> logger)
    {
        _aiProvider = aiProvider ?? throw new ArgumentNullException(nameof(aiProvider));
        _promptTemplateEngine = promptTemplateEngine ?? throw new ArgumentNullException(nameof(promptTemplateEngine));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<AIUnderstandingResult> AnalyzeMessageAsync(
        TelegramMessage message,
        string conversationContext,
        CancellationToken cancellationToken = default)
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        var correlationId = message.Id.ToString();
        _logger.LogInformation("AI Analysis started for message {MessageId}", message.Id);

        var prompt = _promptTemplateEngine.RenderPrompt("v1", message.Content, conversationContext);

        var startPayload = JsonSerializer.Serialize(new { MessageId = message.Id, PromptLength = prompt.Length });
        var startedEvent = new AIAnalysisStarted(Guid.NewGuid(), DateTime.UtcNow, correlationId, "AI_ANALYZER", startPayload);
        await _eventPublisher.PublishAsync(startedEvent, cancellationToken);

        try
        {
            var aiResponse = await _aiProvider.AnalyzeAsync(prompt, cancellationToken);
            _logger.LogInformation("AI Response received for message {MessageId}", message.Id);

            var result = ParseAndValidateResponse(aiResponse);

            var completedPayload = JsonSerializer.Serialize(result);
            var completedEvent = new AIAnalysisCompleted(Guid.NewGuid(), DateTime.UtcNow, correlationId, "AI_ANALYZER", completedPayload);
            await _eventPublisher.PublishAsync(completedEvent, cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI Analyzer failed for message {MessageId}", message.Id);

            var failedPayload = JsonSerializer.Serialize(new { MessageId = message.Id, Error = ex.Message });
            var failedEvent = new AIAnalysisFailed(Guid.NewGuid(), DateTime.UtcNow, correlationId, "AI_ANALYZER", failedPayload);
            await _eventPublisher.PublishAsync(failedEvent, cancellationToken);

            return new AIUnderstandingResult
            {
                Type = "UNKNOWN",
                Confidence = 0.0m,
                Reason = $"AI processing failed: {ex.Message}"
            };
        }
    }

    private AIUnderstandingResult ParseAndValidateResponse(string jsonResponse)
    {
        if (string.IsNullOrWhiteSpace(jsonResponse))
        {
            throw new ArgumentException("AI response was empty.");
        }

        AIUnderstandingResult? result = null;
        try
        {
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true
            };
            result = JsonSerializer.Deserialize<AIUnderstandingResult>(jsonResponse, options);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"AI response returned invalid JSON: {ex.Message}", ex);
        }

        if (result == null)
        {
            throw new InvalidOperationException("AI response deserialized to null.");
        }

        if (string.IsNullOrWhiteSpace(result.Type))
        {
            throw new InvalidOperationException("AI response is missing required field 'type'.");
        }

        var upperType = result.Type.ToUpperInvariant();
        var validTypes = new HashSet<string> { "SIGNAL", "TRADE_UPDATE", "CANCEL_COMMAND", "ANALYSIS", "STATUS_UPDATE", "GENERAL_MESSAGE", "UNKNOWN" };
        if (!validTypes.Contains(upperType))
        {
            throw new InvalidOperationException($"AI response returned invalid message type: '{result.Type}'.");
        }
        result.Type = upperType;

        if (result.Confidence < 0.0m || result.Confidence > 1.0m)
        {
            throw new ArgumentOutOfRangeException(nameof(result.Confidence), "Confidence must be between 0 and 1.");
        }

        if (upperType == "SIGNAL")
        {
            if (result.Entry.HasValue && result.Entry.Value <= 0)
            {
                throw new InvalidOperationException($"Malformed entry price: {result.Entry.Value}. Entry must be greater than zero.");
            }
            if (result.StopLoss.HasValue && result.StopLoss.Value <= 0)
            {
                throw new InvalidOperationException($"Malformed stop loss: {result.StopLoss.Value}. Stop loss must be greater than zero.");
            }
            if (result.TakeProfits != null)
            {
                foreach (var tp in result.TakeProfits)
                {
                    if (tp <= 0)
                    {
                        throw new InvalidOperationException($"Malformed take profit: {tp}. Take profits must be greater than zero.");
                    }
                }
            }
        }

        return result;
    }
}
