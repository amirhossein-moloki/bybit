using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TradingBot.Application.Interfaces.Persistence;
using TradingBot.Application.Monitoring.Configuration;
using TradingBot.Application.Repositories;
using TradingBot.Domain.Entities;
using TradingBot.Telegram.Configuration;
using TradingBot.Telegram.Interfaces;

namespace TradingBot.Telegram.Client;

public class TelegramMonitoredSourcesReporter : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITelegramClient _telegramClient;
    private readonly TelegramOptions _telegramOptions;
    private readonly NotificationOptions _notificationOptions;
    private readonly ILogger<TelegramMonitoredSourcesReporter> _logger;
    private readonly TimeSpan _reportInterval;

    public TelegramMonitoredSourcesReporter(
        IServiceScopeFactory scopeFactory,
        ITelegramClient telegramClient,
        IOptions<TelegramOptions> telegramOptions,
        IOptions<NotificationOptions> notificationOptions,
        ILogger<TelegramMonitoredSourcesReporter> logger,
        TimeSpan? reportInterval = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _telegramClient = telegramClient ?? throw new ArgumentNullException(nameof(telegramClient));
        _telegramOptions = telegramOptions?.Value ?? throw new ArgumentNullException(nameof(telegramOptions));
        _notificationOptions = notificationOptions?.Value ?? throw new ArgumentNullException(nameof(notificationOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _reportInterval = reportInterval ?? TimeSpan.FromMinutes(5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_telegramOptions.Enabled)
        {
            _logger.LogInformation("TelegramMonitoredSourcesReporter: Telegram integration disabled in configuration.");
            return;
        }

        _logger.LogInformation("TelegramMonitoredSourcesReporter: Starting periodic monitored chats reporter (Interval: {Interval} minutes)...", _reportInterval.TotalMinutes);

        // First execution delay or report immediately after small initial delay
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SendMonitoredSourcesReportAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TelegramMonitoredSourcesReporter: Error generating monitored sources report.");
            }

            await Task.Delay(_reportInterval, stoppingToken);
        }

        _logger.LogInformation("TelegramMonitoredSourcesReporter: Stopped.");
    }

    public async Task SendMonitoredSourcesReportAsync(CancellationToken cancellationToken = default)
    {
        var recipient = _notificationOptions.Telegram?.ChatId;
        if (string.IsNullOrWhiteSpace(recipient) || recipient == "-1234567890" || recipient == "1234567890" || recipient == "default-chat-id")
        {
            _logger.LogDebug("TelegramMonitoredSourcesReporter: Main notification recipient ChatId is not configured. Skipping report.");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var sourceRepo = scope.ServiceProvider.GetService<ITelegramSourceRepository>();
        var notifRepo = scope.ServiceProvider.GetService<INotificationRepository>();
        var unitOfWork = scope.ServiceProvider.GetService<TradingBot.Application.Repositories.IUnitOfWork>();

        if (notifRepo == null || unitOfWork == null)
        {
            _logger.LogWarning("TelegramMonitoredSourcesReporter: Notification repository or UnitOfWork is not available.");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("📡 <b>[Telegram Monitored Sources Report]</b>");
        sb.AppendLine($"<b>Time:</b> {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();

        int totalMonitoredCount = 0;

        if (sourceRepo != null)
        {
            var sources = await sourceRepo.GetAllAsync(cancellationToken);
            var monitoredSources = sources.Where(s => s.IsEnabled && !s.IsPaused).ToList();
            totalMonitoredCount = monitoredSources.Count;

            if (monitoredSources.Count > 0)
            {
                sb.AppendLine($"<b>Active DB Monitored Sources ({monitoredSources.Count}):</b>");
                foreach (var source in monitoredSources)
                {
                    var flags = new List<string>();
                    if (source.ProcessMessages) flags.Add("Store");
                    if (source.ListenForSignals) flags.Add("Signals");
                    var flagsStr = flags.Count > 0 ? $" [{string.Join(", ", flags)}]" : string.Empty;

                    sb.AppendLine($"• <b>{source.Title}</b> (ID: <code>{source.TelegramChatId}</code>, Type: {source.Type}){flagsStr}");
                }
            }
            else
            {
                sb.AppendLine("<b>DB Monitored Sources:</b> None active");
            }
        }

        // Fallback or append configured channel list from options if any
        var configuredChannels = _telegramClient.GetMonitoredChannels();
        if (configuredChannels != null && configuredChannels.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"<b>Configured Monitored Channels/Groups ({configuredChannels.Count}):</b>");
            foreach (var ch in configuredChannels)
            {
                sb.AppendLine($"• <code>{ch}</code>");
            }
            if (totalMonitoredCount == 0) totalMonitoredCount = configuredChannels.Count;
        }

        if (totalMonitoredCount == 0)
        {
            sb.AppendLine();
            sb.AppendLine("⚠️ <i>No Telegram channels or groups are currently being monitored.</i>");
        }

        var notification = new Notification(
            eventId: Guid.NewGuid(),
            eventType: "TelegramMonitoredSourcesReport",
            severity: "INFO",
            channel: "Telegram",
            recipient: recipient,
            title: "Telegram Monitored Sources Status Report",
            message: sb.ToString(),
            maxAttempts: _notificationOptions.Telegram?.RetryCount ?? 3
        );

        await notifRepo.AddAsync(notification, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("TelegramMonitoredSourcesReporter: Monitored sources report created and queued for recipient '{Recipient}'.", recipient);
    }
}
