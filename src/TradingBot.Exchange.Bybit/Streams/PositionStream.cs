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

public class BybitPositionStream : IPositionStream
{
    private readonly Channel<PositionUpdateEvent> _channel;
    private readonly IServiceProvider _serviceProvider;

    public BybitPositionStream(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        var options = new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = false
        };
        _channel = Channel.CreateUnbounded<PositionUpdateEvent>(options);
    }

    public void Push(PositionUpdateEvent @event)
    {
        _channel.Writer.TryWrite(@event);
    }

    public async Task SubscribeAsync(CancellationToken cancellationToken = default)
    {
        var client = _serviceProvider.GetRequiredService<IExchangeStreamClient>() as BybitWebSocketClient;
        if (client != null)
        {
            await client.SubscribePrivateAsync("position", cancellationToken);
        }
    }

    public IAsyncEnumerable<PositionUpdateEvent> ReceiveEventsAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
