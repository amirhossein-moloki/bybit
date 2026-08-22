using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Interfaces.Persistence;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.SignalIntelligence.Entities;
using TradingBot.Telegram.Interfaces;
using TradingBot.Telegram.Models;

namespace TradingBot.Telegram.Client;

public class DefaultTelegramMessageReceiver : ITelegramMessageReceiver
{
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly ISignalStorageQueue? _queue;
    private readonly ISignalStorageMetrics? _metrics;
    private readonly ITradingGate? _tradingGate;
    private readonly ILogger<DefaultTelegramMessageReceiver> _logger;

    public DefaultTelegramMessageReceiver(
        IServiceScopeFactory scopeFactory,
        ISignalStorageQueue queue,
        ISignalStorageMetrics metrics,
        ITradingGate tradingGate,
        ILogger<DefaultTelegramMessageReceiver> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _tradingGate = tradingGate ?? throw new ArgumentNullException(nameof(tradingGate));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // Backward compatibility constructor
    public DefaultTelegramMessageReceiver(ILogger<DefaultTelegramMessageReceiver> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _scopeFactory = null;
        _queue = null;
        _metrics = null;
        _tradingGate = null;
    }

    public async Task ReceiveMessageAsync(TelegramMessageDto message)
    {
        _logger.LogInformation("DefaultTelegramMessageReceiver: Received message ID {MessageId} from {ChannelName} (ID: {ChannelId})",
            message.MessageId, message.ChannelName, message.ChannelId);

        if (_tradingGate != null && (_tradingGate.CurrentState == TradingBot.Domain.Enums.ApplicationState.Stopping ||
                                     _tradingGate.CurrentState == TradingBot.Domain.Enums.ApplicationState.Stopped))
        {
            _logger.LogWarning("DefaultTelegramMessageReceiver: Message ID {MessageId} discarded because the application is stopping or stopped.", message.MessageId);
            return;
        }

        if (_scopeFactory == null)
        {
            _logger.LogDebug("DefaultTelegramMessageReceiver: Scope factory is not configured. Skipping processing.");
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();

            // 1. Check Source capabilities in DB if repository registered
            var sourceRepo = scope.ServiceProvider.GetService<ITelegramSourceRepository>();
            if (sourceRepo != null)
            {
                var source = await sourceRepo.GetByChatIdAsync(message.ChannelId);
                if (source == null)
                {
                    var options = scope.ServiceProvider.GetService<Microsoft.Extensions.Options.IOptions<TradingBot.Telegram.Configuration.TelegramOptions>>();
                    var configured = options?.Value?.Channels;
                    if (configured == null || configured.Count == 0)
                    {
                        _logger.LogInformation("DefaultTelegramMessageReceiver: Channel ID {ChannelId} ({ChannelName}) is not registered in TelegramSources. Ignoring message ID {MessageId}.",
                            message.ChannelId, message.ChannelName, message.MessageId);
                        return;
                    }
                }
                else
                {
                    if (!source.IsEnabled || source.IsPaused)
                    {
                        _logger.LogInformation("DefaultTelegramMessageReceiver: Source '{Title}' ({ChatId}) is disabled or paused. Ignoring message ID {MessageId}.",
                            source.Title, source.TelegramChatId, message.MessageId);
                        return;
                    }

                    // Save Telegram Message entity if ProcessMessages is enabled
                    if (source.ProcessMessages)
                    {
                        var msgRepo = scope.ServiceProvider.GetService<IMessageRepository>();
                        if (msgRepo != null)
                        {
                            var domainMsg = new TelegramMessage(
                                source.TelegramChatId,
                                message.MessageId,
                                message.SenderId,
                                message.Text,
                                message.Date
                            );
                            await msgRepo.CreateAsync(domainMsg);
                            _logger.LogDebug("DefaultTelegramMessageReceiver: Persisted message ID {MessageId} for source '{Title}'.", message.MessageId, source.Title);
                        }
                    }

                    if (!source.ListenForSignals)
                    {
                        _logger.LogInformation("DefaultTelegramMessageReceiver: Source '{Title}' has ListenForSignals disabled. Skipping signal analysis for message ID {MessageId}.",
                            source.Title, message.MessageId);
                        return;
                    }
                }
            }

            // 2. Increment Signals Received Metric if available
            _metrics?.IncrementSignalsReceived();

            // 3. Resolve Scoped IMessageFilter to check for signals if queue is configured
            if (_queue == null)
            {
                _logger.LogDebug("DefaultTelegramMessageReceiver: Signal storage queue is not configured. Skipping signal detection.");
                return;
            }

            var messageFilter = scope.ServiceProvider.GetRequiredService<IMessageFilter>();
            var candidate = await messageFilter.AnalyzeAsync(message);

            if (candidate != null)
            {
                _logger.LogInformation("DefaultTelegramMessageReceiver: Detected signal candidate! Symbol: {Symbol}, Side: {Side}, Score: {Score}. Enqueuing for persistence.",
                    candidate.DetectedSymbol, candidate.DetectedSide, candidate.DetectionScore);

                await _queue.EnqueueAsync(candidate);
            }
            else
            {
                _logger.LogDebug("DefaultTelegramMessageReceiver: Message {MessageId} did not qualify as a signal candidate.", message.MessageId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DefaultTelegramMessageReceiver: Error processing received message ID {MessageId}", message.MessageId);
        }
    }
}
