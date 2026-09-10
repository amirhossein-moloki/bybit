using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Monitoring;

namespace TradingBot.Telegram.Interfaces;

public interface ITelegramBotClient
{
    Task<NotificationDeliveryResult> SendTextMessageAsync(
        string botToken,
        string recipient,
        string message,
        CancellationToken cancellationToken = default);
}
