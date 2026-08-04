using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.Application.Interfaces.Streams;
using TradingBot.Application.Models.Events;
using TradingBot.Exchange.Bybit.WebSocket;

namespace TradingBot.Exchange.Bybit.Streams;

public class BybitOrderStream : IOrderStream
{
    private readonly Channel<OrderUpdateEvent> _channel;
    private readonly IServiceProvider _serviceProvider;

    public BybitOrderStream(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        var options = new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = false
        };
        _channel = Channel.CreateUnbounded<OrderUpdateEvent>(options);
    }

    public void Push(OrderUpdateEvent @event)
    {
        _channel.Writer.TryWrite(@event);
    }

    public async Task SubscribeAsync(CancellationToken cancellationToken = default)
    {
        var client = _serviceProvider.GetRequiredService<IExchangeStreamClient>() as BybitWebSocketClient;
        if (client != null)
        {
            await client.SubscribePrivateAsync("order", cancellationToken);
        }
    }

    public IAsyncEnumerable<OrderUpdateEvent> ReceiveEventsAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
