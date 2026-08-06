using System.Threading.Tasks;
using TradingBot.Telegram.Models;
using TradingBot.Application.Models;

namespace TradingBot.Application.Interfaces;

public interface IMessageFilter
{
    Task<SignalCandidate?> AnalyzeAsync(TelegramMessageDto message);
}
