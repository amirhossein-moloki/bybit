using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TradingBot.Worker;

public class TradingBotWorkerService : BackgroundService
{
    private readonly ILogger<TradingBotWorkerService> _logger;

    public TradingBotWorkerService(ILogger<TradingBotWorkerService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TradingBot Background Worker is starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogDebug("TradingBot Worker is running in the background...");

            // Simulating polling, check-ins, or message loop
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }

        _logger.LogInformation("TradingBot Background Worker is stopping.");
    }
}
