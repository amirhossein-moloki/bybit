using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Parser.Models;

namespace TradingBot.Parser.Interfaces;

public interface IAIDecisionEngine
{
    AIProcessingDecision DetermineAIUsage(TelegramMessage message, ParsedMessageResult? ruleBasedResult);
}
