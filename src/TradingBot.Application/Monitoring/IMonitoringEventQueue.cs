using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Monitoring;

public interface IMonitoringEventQueue
{
    ValueTask EnqueueAsync(MonitoringEvent @event, CancellationToken cancellationToken = default);
    ValueTask<MonitoringEvent> DequeueAsync(CancellationToken cancellationToken = default);
}
