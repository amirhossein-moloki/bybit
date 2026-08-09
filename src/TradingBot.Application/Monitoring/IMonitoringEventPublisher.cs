using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Monitoring;

public interface IMonitoringEventPublisher
{
    Task PublishAsync(MonitoringEvent @event, bool forceSynchronous = false, CancellationToken cancellationToken = default);
}
