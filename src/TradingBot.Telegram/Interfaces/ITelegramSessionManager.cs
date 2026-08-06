using System.IO;

namespace TradingBot.Telegram.Interfaces;

public interface ITelegramSessionManager
{
    Stream LoadSession();
    void SaveSession(Stream sessionStream);
    void DeleteSession();
    bool SessionExists();
}
