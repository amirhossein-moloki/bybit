using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Models;
using TradingBot.Domain.SignalIntelligence.Models;

namespace TradingBot.Application.SignalIntelligence.Contracts;

public interface IMessageContextBuilder
{
    Task<MessageIntelligenceContext> BuildContextAsync(TelegramMessageDto message, CancellationToken cancellationToken = default);
}

public interface IDeterministicRuleEngine
{
    Task<StructuredIntent?> EvaluateAsync(MessageIntelligenceContext context, CancellationToken cancellationToken = default);
}

public interface IAIIntentInterpreter
{
    Task<StructuredIntent> InterpretAsync(MessageIntelligenceContext context, CancellationToken cancellationToken = default);
}

public interface ITargetResolver
{
    Task<StructuredIntent> ResolveTargetsAsync(StructuredIntent intent, MessageIntelligenceContext context, CancellationToken cancellationToken = default);
}

public interface IIntentSafetyValidator
{
    Task<(bool IsSafe, string Reason)> ValidateSafetyAsync(StructuredIntent intent, MessageIntelligenceContext context, CancellationToken cancellationToken = default);
}

public interface IIntentExecutionRouter
{
    Task ProcessMessageIntelligenceAsync(TelegramMessageDto message, CancellationToken cancellationToken = default);
}
