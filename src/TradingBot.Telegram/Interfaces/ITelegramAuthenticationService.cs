using System.Threading.Tasks;

namespace TradingBot.Telegram.Interfaces;

public interface ITelegramAuthenticationService
{
    Task AuthenticateAsync();
    Task<bool> IsAuthenticatedAsync();
}
