using System;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Interfaces.Streams;
using TradingBot.Application.Trading.Execution.Contracts;
using TradingBot.Exchange.Bybit;
using TradingBot.Exchange.Bybit.Streams;
using TradingBot.Exchange.Bybit.WebSocket;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddBybitExchange(this IServiceCollection services, Action<BybitSettings> configure)
    {
        if (configure == null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        var settings = new BybitSettings();
        configure(settings);
        services.AddSingleton(settings);

        services.AddHttpClient<IExchangeClient, BybitExchangeClient>();
        services.AddHttpClient<IExchangeTradingGateway, BybitExecutionAdapter>();

        // Register WebSockets and Stream Clients
        services.AddSingleton<SubscriptionManager>();
        services.AddSingleton<MessageHandler>();

        services.AddSingleton<IMarketStream, BybitMarketStream>();
        services.AddSingleton<IOrderStream, BybitOrderStream>();
        services.AddSingleton<IPositionStream, BybitPositionStream>();

        services.AddSingleton<IExchangeStreamClient, BybitWebSocketClient>();

        return services;
    }
}
