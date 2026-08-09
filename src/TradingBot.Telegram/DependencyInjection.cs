using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.Telegram.Authentication;
using TradingBot.Telegram.Client;
using TradingBot.Telegram.Configuration;
using TradingBot.Telegram.Health;
using TradingBot.Telegram.Interfaces;
using TradingBot.Telegram;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddTelegramIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection("Telegram");
        services.Configure<TelegramOptions>(section);

        services.PostConfigure<TelegramOptions>(options =>
        {
            var envApiId = Environment.GetEnvironmentVariable("TELEGRAM_API_ID");
            if (!string.IsNullOrEmpty(envApiId))
            {
                options.ApiId = envApiId;
            }

            var envApiHash = Environment.GetEnvironmentVariable("TELEGRAM_API_HASH");
            if (!string.IsNullOrEmpty(envApiHash))
            {
                options.ApiHash = envApiHash;
            }

            var envPhone = Environment.GetEnvironmentVariable("TELEGRAM_PHONE");
            if (!string.IsNullOrEmpty(envPhone))
            {
                options.PhoneNumber = envPhone;
            }

            var envSessionPath = Environment.GetEnvironmentVariable("TELEGRAM_SESSION_PATH");
            if (!string.IsNullOrEmpty(envSessionPath))
            {
                options.SessionPath = envSessionPath;
            }
        });

        services.AddSingleton<ITelegramSessionManager, TelegramSessionManager>();
        services.AddSingleton<ITelegramMessageReceiver, DefaultTelegramMessageReceiver>();
        services.AddSingleton<ITelegramClient, TelegramClientService>();
        services.AddSingleton<ITelegramAuthenticationService, TelegramAuthService>();
        services.AddSingleton<TradingBot.Application.Monitoring.INotificationChannel, TelegramNotificationChannel>();

        services.AddHostedService<TelegramListenerWorker>();

        services.AddHealthChecks().AddCheck<TelegramHealthCheck>("Telegram");

        return services;
    }
}
