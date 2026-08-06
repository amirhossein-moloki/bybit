using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Telegram.Interfaces;
using TradingBot.Telegram.Models;

namespace TradingBot.Telegram.Client;

public class DefaultTelegramMessageReceiver : ITelegramMessageReceiver
{
    private readonly ILogger<DefaultTelegramMessageReceiver> _logger;

    public DefaultTelegramMessageReceiver(ILogger<DefaultTelegramMessageReceiver> logger)
    {
        _logger = logger;
    }

    public Task ReceiveMessageAsync(TelegramMessageDto message)
    {
        _logger.LogInformation("DefaultTelegramMessageReceiver: Message ID {MessageId} from {ChannelName} (ID: {ChannelId}): {Text}",
            message.MessageId, message.ChannelName, message.ChannelId, message.Text);
        return Task.CompletedTask;
    }
}
