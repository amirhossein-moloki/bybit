using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TradingBot.Application.SignalIntelligence.Contracts;
using TradingBot.Domain.SignalIntelligence.Interfaces;

namespace TradingBot.Application.SignalIntelligence.Services;

public class IntelligenceEventPublisher : IIntelligenceEventPublisher
{
    private readonly IEnumerable<IIntelligenceEventHandler> _handlers;
    private readonly ILogger<IntelligenceEventPublisher> _logger;

    public IntelligenceEventPublisher(
        IEnumerable<IIntelligenceEventHandler> handlers,
        ILogger<IntelligenceEventPublisher> logger)
    {
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task PublishAsync(IIntelligenceEvent @event, CancellationToken cancellationToken = default)
    {
        if (@event == null) return;

        _logger.LogInformation("Publishing intelligence event {EventType}, EventId: {EventId}",
            @event.GetType().Name, @event.EventId);

        foreach (var handler in _handlers)
        {
            try
            {
                await handler.HandleAsync(@event, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle intelligence event {EventType} in handler {HandlerName}",
                    @event.GetType().Name, handler.GetType().Name);
            }
        }
    }
}
