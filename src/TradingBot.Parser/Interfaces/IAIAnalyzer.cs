using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Parser.Models;

namespace TradingBot.Parser.Interfaces;

public interface IAIAnalyzer
{
    Task<AIUnderstandingResult> AnalyzeMessageAsync(TelegramMessage message, string conversationContext, CancellationToken cancellationToken = default);
}
