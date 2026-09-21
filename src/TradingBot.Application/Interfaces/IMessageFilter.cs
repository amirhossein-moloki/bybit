using System.Threading.Tasks;
using TradingBot.Application.Models;

namespace TradingBot.Application.Interfaces;

public interface IMessageFilter
{
    Task<SignalCandidate?> AnalyzeAsync(TelegramMessageDto message);
}
