using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.Entities;

namespace TradingBot.Application.Monitoring;

public interface IAlertEngine
{
    Task<bool> ProcessEventAsync(MonitoringEvent @event, CancellationToken cancellationToken = default);
    Task EvaluateActiveAlertsAsync(CancellationToken cancellationToken = default);
}
