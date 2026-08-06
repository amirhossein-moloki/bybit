using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Models;
using TradingBot.Application.Services;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.AddScoped<ISignalProcessor, SignalProcessor>();

        if (configuration != null)
        {
            services.Configure<SignalDetectionSettings>(configuration.GetSection("SignalDetection"));
        }
        else
        {
            services.Configure<SignalDetectionSettings>(_ => { });
        }

        services.AddScoped<IMessageFilter, MessageFilterService>();

        return services;
    }
}
