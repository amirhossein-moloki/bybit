using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.SignalIntelligence.Entities;

namespace TradingBot.Application.SignalIntelligence.Contracts;

public interface IMessageClassifier
{
    Task<MessageAnalysis> ClassifyAsync(TelegramMessage message, CancellationToken cancellationToken = default);
}
