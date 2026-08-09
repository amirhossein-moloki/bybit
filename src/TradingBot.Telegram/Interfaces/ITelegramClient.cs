using System.Threading.Tasks;
using TradingBot.Telegram.Models;

namespace TradingBot.Telegram.Interfaces;

public interface ITelegramClient
{
    Task ConnectAsync();
    Task DisconnectAsync();
    bool IsConnected();
    TelegramConnectionState CurrentState { get; }
    Task InitializeListeningAsync();
    Task SendMessageAsync(long chatId, string message);
}
