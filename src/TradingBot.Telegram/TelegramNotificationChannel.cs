using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBot.Application.Monitoring;
using TradingBot.Domain.Entities;
using TradingBot.Telegram.Configuration;
using TradingBot.Telegram.Interfaces;

namespace TradingBot.Telegram;

public class TelegramNotificationChannel : INotificationChannel
{
    private readonly ITelegramClient _telegramClient;
    private readonly TelegramOptions _telegramOptions;
    private readonly ILogger<TelegramNotificationChannel> _logger;

    public string ChannelName => "Telegram";

    public TelegramNotificationChannel(
        ITelegramClient telegramClient,
        IOptions<TelegramOptions> telegramOptions,
        ILogger<TelegramNotificationChannel> logger)
    {
        _telegramClient = telegramClient ?? throw new ArgumentNullException(nameof(telegramClient));
        _telegramOptions = telegramOptions?.Value ?? throw new ArgumentNullException(nameof(telegramOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<NotificationDeliveryResult> SendAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        if (!_telegramOptions.Enabled)
        {
            _logger.LogWarning("TelegramNotificationChannel: Telegram integration is globally disabled in configuration.");
            return NotificationDeliveryResult.AsFailure(isRetryable: false, "TELEGRAM_DISABLED", "Telegram integration is globally disabled.");
        }

        if (!long.TryParse(notification.Recipient, out var chatId))
        {
            _logger.LogError("TelegramNotificationChannel: Invalid Recipient format '{Recipient}'. Must be a valid long ChatId.", notification.Recipient);
            return NotificationDeliveryResult.AsFailure(isRetryable: false, "INVALID_RECIPIENT", $"Recipient format '{notification.Recipient}' is invalid.");
        }

        try
        {
            // Ensure connection
            if (!_telegramClient.IsConnected())
            {
                _logger.LogInformation("TelegramNotificationChannel: Client is not connected. Attempting to connect...");
                await _telegramClient.ConnectAsync();
            }

            _logger.LogInformation("TelegramNotificationChannel: Sending message to Chat {ChatId}...", chatId);
            await _telegramClient.SendMessageAsync(chatId, notification.Message);

            _logger.LogInformation("TelegramNotificationChannel: Message delivered successfully to Chat {ChatId}.", chatId);
            return NotificationDeliveryResult.AsSuccess();
        }
        catch (TimeoutException tex)
        {
            _logger.LogWarning(tex, "TelegramNotificationChannel: Timeout sending message to Chat {ChatId}.", chatId);
            return NotificationDeliveryResult.AsFailure(isRetryable: true, "TIMEOUT", tex.Message);
        }
        catch (System.Net.Http.HttpRequestException hex)
        {
            _logger.LogWarning(hex, "TelegramNotificationChannel: Network error sending message to Chat {ChatId}.", chatId);
            return NotificationDeliveryResult.AsFailure(isRetryable: true, "NETWORK_ERROR", hex.Message);
        }
        catch (Exception ex)
        {
            var exceptionTypeName = ex.GetType().Name;
            var isRetryable = true;
            var errorCode = "TELEGRAM_ERROR";

            // Inspect WTelegram specific RpcException if applicable, or check the error message directly for permanent failures
            var errorMsg = ex.Message;
            if (ex.GetType().FullName == "TL.RpcException" ||
                exceptionTypeName == "RpcException" ||
                errorMsg.Contains("PEER_ID_INVALID") ||
                errorMsg.Contains("CHAT_ID_INVALID") ||
                errorMsg.Contains("CHAT_WRITE_FORBIDDEN") ||
                errorMsg.Contains("USER_DEACTIVATED"))
            {
                // Permanent errors
                isRetryable = false;
                errorCode = "PERMANENT_TELEGRAM_ERROR";
            }

            _logger.LogError(ex, "TelegramNotificationChannel: Failed to deliver message to Chat {ChatId}. Retryable: {IsRetryable}", chatId, isRetryable);
            return NotificationDeliveryResult.AsFailure(isRetryable, errorCode, errorMsg);
        }
    }
}
