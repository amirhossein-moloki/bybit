using System.Threading;
using System.Threading.Tasks;
using TradingBot.Domain.SignalIntelligence.Interfaces;

namespace TradingBot.Application.SignalIntelligence.Contracts;

public interface IIntelligenceEventPublisher
{
    Task PublishAsync(IIntelligenceEvent @event, CancellationToken cancellationToken = default);
}

public interface IIntelligenceEventHandler
{
    Task HandleAsync(IIntelligenceEvent @event, CancellationToken cancellationToken = default);
}
