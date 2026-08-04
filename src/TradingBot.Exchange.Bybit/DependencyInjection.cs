using Microsoft.Extensions.DependencyInjection;
using TradingBot.Application.Interfaces;
using TradingBot.Exchange.Bybit;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddBybitExchange(this IServiceCollection services)
    {
        services.AddScoped<IExchangeClient, BybitExchangeClient>();

        return services;
    }
}
