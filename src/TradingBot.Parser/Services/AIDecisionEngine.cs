using System;
using System.Text.RegularExpressions;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Domain.SignalIntelligence.Enums;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Parser.Interfaces;
using TradingBot.Parser.Models;

namespace TradingBot.Parser.Services;

public class AIDecisionEngine : IAIDecisionEngine
{
    public AIProcessingDecision DetermineAIUsage(TelegramMessage message, ParsedMessageResult? ruleBasedResult)
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        var text = message.Content ?? string.Empty;

        // Reply context present -> AI contextual analysis needed
        if (message.ReplyToMessageId.HasValue && message.ReplyToMessageId.Value > 0)
        {
            return new AIProcessingDecision
            {
                ShouldUseAI = true,
                Reason = "Message contains ReplyToMessageId reference requiring contextual AI resolution."
            };
        }

        // Implicit or natural language commands/phrasing in Persian or English
        if (ContainsNaturalLanguageOrConditionalPhrasing(text))
        {
            return new AIProcessingDecision
            {
                ShouldUseAI = true,
                Reason = "Message contains natural language, implicit command, or conditional phrasing requiring AI understanding."
            };
        }

        if (ruleBasedResult == null || ruleBasedResult.Type == MessageType.UNKNOWN)
        {
            return new AIProcessingDecision
            {
                ShouldUseAI = true,
                Reason = "Rule-based parser returned UNKNOWN classification or was null."
            };
        }

        if (ruleBasedResult.Confidence < 0.70m)
        {
            return new AIProcessingDecision
            {
                ShouldUseAI = true,
                Reason = $"Rule-based parser confidence ({ruleBasedResult.Confidence}) is below the required threshold of 0.70."
            };
        }

        if (ruleBasedResult.Type == MessageType.SIGNAL)
        {
            if (string.IsNullOrEmpty(ruleBasedResult.Symbol) ||
                ruleBasedResult.Side == null ||
                (ruleBasedResult.Entry == null && ruleBasedResult.EntryRangeMin == null) ||
                ruleBasedResult.StopLoss == null)
            {
                return new AIProcessingDecision
                {
                    ShouldUseAI = true,
                    Reason = "Rule-based parser is missing critical SIGNAL fields (Symbol, Side, Entry, or StopLoss)."
                };
            }
        }

        if (ruleBasedResult.Type == MessageType.TRADE_UPDATE || ruleBasedResult.Type == MessageType.CANCEL_COMMAND)
        {
            if (ruleBasedResult.Action == null || string.IsNullOrEmpty(ruleBasedResult.Symbol))
            {
                return new AIProcessingDecision
                {
                    ShouldUseAI = true,
                    Reason = $"Rule-based parser is missing critical {ruleBasedResult.Type} fields (Action or Symbol)."
                };
            }
        }

        return new AIProcessingDecision
        {
            ShouldUseAI = false,
            Reason = $"Rule-based parser successfully parsed message with type {ruleBasedResult.Type} and confidence {ruleBasedResult.Confidence}."
        };
    }

    private static bool ContainsNaturalLanguageOrConditionalPhrasing(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var triggers = new[]
        {
            "ببندش", "ریسک فریش کن", "فعلاً وارد نشید", "وارد نشید", "همین رو نگه دارید",
            "صبر کنید", "الان میشه وارد شد", "واکنش داده", "اگر", "اگه", "تا قبل از",
            "در صورت", "چنانچه", "سر به سر"
        };

        foreach (var trigger in triggers)
        {
            if (text.Contains(trigger, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
