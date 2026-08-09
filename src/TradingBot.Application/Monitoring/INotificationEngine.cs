using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Monitoring;

public interface INotificationEngine
{
    Task ProcessEventAsync(MonitoringEvent @event, CancellationToken cancellationToken = default);
}
