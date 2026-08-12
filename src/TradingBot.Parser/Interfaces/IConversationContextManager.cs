using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Parser.Models;

namespace TradingBot.Parser.Interfaces;

public interface IConversationContextManager
{
    Task<ConversationContext> GetContextAsync(long channelId, CancellationToken cancellationToken = default);
    string GetContextSummary(ConversationContext context);
}
