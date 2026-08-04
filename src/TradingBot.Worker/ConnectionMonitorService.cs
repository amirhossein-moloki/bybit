using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBot.Application.Interfaces.Streams;

namespace TradingBot.Worker;

public class ConnectionMonitorService : BackgroundService
{
    private readonly IExchangeStreamClient _streamClient;
    private readonly ILogger<ConnectionMonitorService> _logger;

    public ConnectionMonitorService(
        IExchangeStreamClient streamClient,
        ILogger<ConnectionMonitorService> logger)
    {
        _streamClient = streamClient ?? throw new ArgumentNullException(nameof(streamClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        _logger.LogInformation("ConnectionMonitorService: Starting stream connection...");

        try
        {
            await _streamClient.ConnectAsync(stoppingToken);
            _logger.LogInformation("ConnectionMonitorService: Exchange stream client connected.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ConnectionMonitorService: Initial connection failed.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            // Periodically check or monitor health/state
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }

        _logger.LogInformation("ConnectionMonitorService: Stopping stream connection...");
        await _streamClient.DisconnectAsync(CancellationToken.None);
    }
}
