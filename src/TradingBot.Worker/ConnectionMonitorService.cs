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
    private readonly TradingBot.Application.Monitoring.IWorkerHealthRegistry _healthRegistry;

    public ConnectionMonitorService(
        IExchangeStreamClient streamClient,
        ILogger<ConnectionMonitorService> logger,
        TradingBot.Application.Monitoring.IWorkerHealthRegistry healthRegistry)
    {
        _streamClient = streamClient ?? throw new ArgumentNullException(nameof(streamClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _healthRegistry = healthRegistry ?? throw new ArgumentNullException(nameof(healthRegistry));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        _healthRegistry.RegisterWorker(nameof(ConnectionMonitorService), isCritical: false);
        _logger.LogInformation("ConnectionMonitorService: Starting stream connection...");

        try
        {
            await _streamClient.ConnectAsync(stoppingToken);
            _logger.LogInformation("ConnectionMonitorService: Exchange stream client connected.");
        }
        catch (Exception ex)
        {
            _healthRegistry.RecordHeartbeat(nameof(ConnectionMonitorService), "Failed", ex.Message);
            _logger.LogError(ex, "ConnectionMonitorService: Initial connection failed.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            _healthRegistry.RecordHeartbeat(nameof(ConnectionMonitorService), "Running");
            // Periodically check or monitor health/state
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }

        _healthRegistry.RecordHeartbeat(nameof(ConnectionMonitorService), "Stopping");
        _logger.LogInformation("ConnectionMonitorService: Stopping stream connection...");
        await _streamClient.DisconnectAsync(CancellationToken.None);
        _healthRegistry.RecordHeartbeat(nameof(ConnectionMonitorService), "Stopped");
    }
}
