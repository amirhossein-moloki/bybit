using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Interfaces;
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

        // 1. Increment Signals Received Metric if available
        _metrics?.IncrementSignalsReceived();

        // 2. Resolve Scoped IMessageFilter to check for signals if dependencies are present
        if (_scopeFactory == null || _queue == null)
        {
            _logger.LogDebug("DefaultTelegramMessageReceiver: Queue or scope factory is not configured. Skipping signal detection.");
            return;
        }

        try
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var messageFilter = scope.ServiceProvider.GetRequiredService<IMessageFilter>();
                var candidate = await messageFilter.AnalyzeAsync(message);

                if (candidate != null)
                {
                    _logger.LogInformation("DefaultTelegramMessageReceiver: Detected signal candidate! Symbol: {Symbol}, Side: {Side}, Score: {Score}. Enqueuing for persistence.",
                        candidate.DetectedSymbol, candidate.DetectedSide, candidate.DetectionScore);

                    // 3. Enqueue to Storage Queue
                    await _queue.EnqueueAsync(candidate);
                }
                else
                {
                    _logger.LogDebug("DefaultTelegramMessageReceiver: Message {MessageId} did not qualify as a signal candidate.", message.MessageId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DefaultTelegramMessageReceiver: Error processing received message ID {MessageId}", message.MessageId);
        }
    }
}
