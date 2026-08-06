using System.Threading.Tasks;
using TradingBot.Telegram.Models;

namespace TradingBot.Telegram.Interfaces;

public interface ITelegramMessageReceiver
{
    Task ReceiveMessageAsync(TelegramMessageDto message);
}
