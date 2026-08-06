using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Models;

namespace TradingBot.Worker;

public class SignalStorageWorker : BackgroundService
{
    private readonly ISignalStorageQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SignalStorageWorker> _logger;

    public SignalStorageWorker(
        ISignalStorageQueue queue,
        IServiceProvider serviceProvider,
        ILogger<SignalStorageWorker> logger)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // Let caller yield and continue starting other workers

        _logger.LogInformation("SignalStorageWorker: Starting queue consumer background loop...");

        while (!stoppingToken.IsCancellationRequested)
        {
            SignalCandidate? candidate = null;
            try
            {
                // 1. Dequeue next candidate (blocks until available)
                candidate = await _queue.DequeueAsync(stoppingToken);

                _logger.LogDebug("SignalStorageWorker: Dequeued signal candidate. Channel: {ChannelId}, MessageId: {MessageId}",
                    candidate.ChannelId, candidate.MessageId);

                // 2. Process within scoped container to resolve Scoped DB contexts and repositories
                using (var scope = _serviceProvider.CreateScope())
                {
                    var storageService = scope.ServiceProvider.GetRequiredService<ISignalStorageService>();
                    await storageService.StoreAsync(candidate);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("SignalStorageWorker: Stopping queue consumer due to cancellation...");
                break;
            }
            catch (Exception ex)
            {
                if (candidate != null)
                {
                    _logger.LogError(ex, "SignalStorageWorker: Failed to store signal candidate. Channel: {ChannelId}, MessageId: {MessageId}. Continuing queue processing.",
                        candidate.ChannelId, candidate.MessageId);
                }
                else
                {
                    _logger.LogError(ex, "SignalStorageWorker: Exception occurred in worker loop. Continuing queue processing.");
                }
            }
        }

        _logger.LogInformation("SignalStorageWorker: Queue consumer background loop stopped.");
    }
}
