using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.SignalIntelligence.Entities;

namespace TradingBot.Application.SignalIntelligence.Contracts;

public interface IMessageParser
{
    Task<ParsedMessageResult> ParseAsync(
        TelegramMessage message,
        CancellationToken cancellationToken = default);
}
