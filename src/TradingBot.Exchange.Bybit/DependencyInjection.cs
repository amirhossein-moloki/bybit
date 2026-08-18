using System;
using System.Net;
using System.Net.Http;
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

        void ConfigureHttpClientProxy(IHttpClientBuilder builder)
        {
            if (!string.IsNullOrWhiteSpace(settings.ProxyUrl) && Uri.TryCreate(settings.ProxyUrl, UriKind.Absolute, out var proxyUri))
            {
                builder.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    Proxy = new WebProxy(proxyUri),
                    UseProxy = true
                });
            }
        }

        ConfigureHttpClientProxy(services.AddHttpClient<IExchangeClient, BybitExchangeClient>());
        ConfigureHttpClientProxy(services.AddHttpClient<IExchangeTradingGateway, BybitExecutionAdapter>());
        ConfigureHttpClientProxy(services.AddHttpClient<IPositionGateway, PositionGateway>());

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
