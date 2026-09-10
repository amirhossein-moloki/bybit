using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Monitoring;
using TradingBot.Telegram.Interfaces;

namespace TradingBot.Telegram.Client;

public class TelegramBotClient : ITelegramBotClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<TelegramBotClient> _logger;

    public TelegramBotClient(HttpClient httpClient, ILogger<TelegramBotClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<NotificationDeliveryResult> SendTextMessageAsync(
        string botToken,
        string recipient,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(botToken))
        {
            return NotificationDeliveryResult.AsFailure(isRetryable: false, "MISSING_BOT_TOKEN", "Telegram BotToken is not provided.");
        }

        if (string.IsNullOrWhiteSpace(recipient))
        {
            return NotificationDeliveryResult.AsFailure(isRetryable: false, "INVALID_RECIPIENT", "Telegram recipient/chat_id is empty.");
        }

        var url = $"https://api.telegram.org/bot{botToken}/sendMessage";

        object chatIdObj = long.TryParse(recipient, out var parsedId) ? parsedId : recipient;

        var payload = new
        {
            chat_id = chatIdObj,
            text = message
        };

        var json = JsonSerializer.Serialize(payload);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            _logger.LogInformation("TelegramBotClient: Sending message to Bot API for chat {Recipient}...", recipient);
            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("TelegramBotClient: Successfully delivered message via Telegram Bot API to chat {Recipient}.", recipient);
                return NotificationDeliveryResult.AsSuccess();
            }

            _logger.LogWarning("TelegramBotClient: Bot API returned status code {StatusCode}. Body: {ResponseBody}", response.StatusCode, responseBody);

            if (response.StatusCode == HttpStatusCode.BadRequest ||
                response.StatusCode == HttpStatusCode.Forbidden ||
                response.StatusCode == HttpStatusCode.NotFound)
            {
                var errorCode = responseBody.Contains("chat not found", StringComparison.OrdinalIgnoreCase)
                    ? "CHAT_NOT_FOUND"
                    : "PERMANENT_TELEGRAM_ERROR";

                return NotificationDeliveryResult.AsFailure(isRetryable: false, errorCode, $"Telegram Bot API error ({response.StatusCode}): {responseBody}");
            }

            if ((int)response.StatusCode == 429)
            {
                return NotificationDeliveryResult.AsFailure(isRetryable: true, "FLOOD_WAIT", $"Telegram Bot API rate limit (429): {responseBody}");
            }

            return NotificationDeliveryResult.AsFailure(isRetryable: true, "TELEGRAM_ERROR", $"Telegram Bot API error ({response.StatusCode}): {responseBody}");
        }
        catch (TaskCanceledException tex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(tex, "TelegramBotClient: Timeout sending message via Bot API to chat {Recipient}.", recipient);
            return NotificationDeliveryResult.AsFailure(isRetryable: true, "TIMEOUT", tex.Message);
        }
        catch (HttpRequestException hex)
        {
            _logger.LogWarning(hex, "TelegramBotClient: Network error sending message via Bot API to chat {Recipient}.", recipient);
            return NotificationDeliveryResult.AsFailure(isRetryable: true, "NETWORK_ERROR", hex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TelegramBotClient: Unexpected error sending message via Bot API to chat {Recipient}.", recipient);
            return NotificationDeliveryResult.AsFailure(isRetryable: true, "UNEXPECTED_ERROR", ex.Message);
        }
    }
}
