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

public class BybitMarketStream : IMarketStream
{
    private readonly Channel<MarketTickerUpdateEvent> _channel;
    private readonly IServiceProvider _serviceProvider;

    public BybitMarketStream(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        var options = new UnboundedChannelOptions
        {
            SingleWriter = true,
            SingleReader = false
        };
        _channel = Channel.CreateUnbounded<MarketTickerUpdateEvent>(options);
    }

    public void Push(MarketTickerUpdateEvent @event)
    {
        _channel.Writer.TryWrite(@event);
    }

    public async Task SubscribeAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var client = _serviceProvider.GetRequiredService<IExchangeStreamClient>() as BybitWebSocketClient;
        if (client != null)
        {
            await client.SubscribePublicAsync($"tickers.{symbol.ToUpperInvariant()}", cancellationToken);
        }
    }

    public IAsyncEnumerable<MarketTickerUpdateEvent> ReceiveEventsAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }
}
