using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Monitoring.Services;

public class MonitoringEventQueue : IMonitoringEventQueue
{
    private readonly Channel<MonitoringEvent> _channel;

    public MonitoringEventQueue()
    {
        // Bounded channel to prevent unbounded memory growth (Section 55 & 69)
        var options = new BoundedChannelOptions(10000)
        {
            SingleWriter = false,
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest // Drop oldest if queue overflows to avoid blocking critical trading
        };
        _channel = Channel.CreateBounded<MonitoringEvent>(options);
    }

    public ValueTask EnqueueAsync(MonitoringEvent @event, CancellationToken cancellationToken = default)
    {
        if (@event == null) return ValueTask.CompletedTask;
        return _channel.Writer.WriteAsync(@event, cancellationToken);
    }

    public ValueTask<MonitoringEvent> DequeueAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAsync(cancellationToken);
    }
}
