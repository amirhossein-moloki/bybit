using TradingBot.Domain.Entities;

namespace TradingBot.Application.Monitoring;

public interface INotificationPolicy
{
    bool ShouldNotify(MonitoringEvent @event);
}
