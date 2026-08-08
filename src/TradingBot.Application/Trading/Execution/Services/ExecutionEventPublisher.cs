using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Application.Trading.Execution.Events;

namespace TradingBot.Application.Trading.Execution.Services;

public class ExecutionEventPublisher : IExecutionEventPublisher
{
    private readonly IEnumerable<IExecutionEventHandler> _handlers;

    public ExecutionEventPublisher(IEnumerable<IExecutionEventHandler> handlers)
    {
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
    }

    public async Task PublishAsync(IExecutionEvent @event, CancellationToken cancellationToken = default)
    {
        if (@event == null) return;

        foreach (var handler in _handlers)
        {
            await handler.HandleAsync(@event, cancellationToken);
        }
    }
}
