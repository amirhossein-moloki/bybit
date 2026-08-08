using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Trading.Execution.Events;

namespace TradingBot.Application.Trading.Execution.Contracts;

public interface IExecutionEventPublisher
{
    Task PublishAsync(IExecutionEvent @event, CancellationToken cancellationToken = default);
}
