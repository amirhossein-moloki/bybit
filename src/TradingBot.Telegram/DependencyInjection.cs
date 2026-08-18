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
            var envApiId = Environment.GetEnvironmentVariable("TELEGRAM_API_ID")
                           ?? Environment.GetEnvironmentVariable("Telegram__ApiId");
            if (!string.IsNullOrWhiteSpace(envApiId) && string.IsNullOrWhiteSpace(options.ApiId))
            {
                options.ApiId = envApiId.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(envApiId))
            {
                options.ApiId = envApiId.Trim();
            }

            var envApiHash = Environment.GetEnvironmentVariable("TELEGRAM_API_HASH")
                             ?? Environment.GetEnvironmentVariable("Telegram__ApiHash");
            if (!string.IsNullOrWhiteSpace(envApiHash) && string.IsNullOrWhiteSpace(options.ApiHash))
            {
                options.ApiHash = envApiHash.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(envApiHash))
            {
                options.ApiHash = envApiHash.Trim();
            }

            var envPhone = Environment.GetEnvironmentVariable("TELEGRAM_PHONE")
                           ?? Environment.GetEnvironmentVariable("Telegram__PhoneNumber");
            if (!string.IsNullOrWhiteSpace(envPhone))
            {
                options.PhoneNumber = envPhone.Trim();
            }

            var envSessionPath = Environment.GetEnvironmentVariable("TELEGRAM_SESSION_PATH")
                                 ?? Environment.GetEnvironmentVariable("Telegram__SessionPath");
            if (!string.IsNullOrWhiteSpace(envSessionPath))
            {
                options.SessionPath = envSessionPath.Trim();
            }
        });

        services.AddSingleton<ITelegramSessionManager, TelegramSessionManager>();
        services.AddSingleton<ITelegramMessageReceiver, DefaultTelegramMessageReceiver>();
        services.AddSingleton<ITelegramClient, TelegramClientService>();
        services.AddSingleton<ITelegramAuthenticationService, TelegramAuthService>();
        services.AddSingleton<ITelegramQrAuthService, TelegramQrAuthService>();
        services.AddSingleton<TradingBot.Application.Monitoring.INotificationChannel, TelegramNotificationChannel>();

        services.AddHostedService<TelegramListenerWorker>();

        services.AddHealthChecks().AddCheck<TelegramHealthCheck>("Telegram");

        return services;
    }
}
