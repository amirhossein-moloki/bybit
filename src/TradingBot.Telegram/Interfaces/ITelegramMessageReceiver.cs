using System.Threading.Tasks;
using TradingBot.Application.Models;

namespace TradingBot.Telegram.Interfaces;

public interface ITelegramMessageReceiver
{
    Task ReceiveMessageAsync(TelegramMessageDto message);
}
