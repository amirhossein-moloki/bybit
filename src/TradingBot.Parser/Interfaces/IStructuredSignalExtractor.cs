using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Parser.Models;

namespace TradingBot.Parser.Interfaces;

public interface IStructuredSignalExtractor
{
    Task<SignalExtractionResult> ExtractAsync(TelegramMessage message, CancellationToken cancellationToken = default);
}
