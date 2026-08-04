using System;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.Application.Interfaces;
using TradingBot.Exchange.Bybit;

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

        return services;
    }
}
