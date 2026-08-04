using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.Application.Interfaces.Persistence;
using TradingBot.Infrastructure.Configuration;
using TradingBot.Infrastructure.Health;
using TradingBot.Infrastructure.Persistence;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind and register Settings
        var settings = new TradingBotSettings();
        configuration.Bind(settings);
        services.AddSingleton(settings);

        // Bind sub-sections for individual IOptions if needed
        services.Configure<ApplicationSettings>(configuration.GetSection("Application"));
        services.Configure<DatabaseSettings>(configuration.GetSection("Database"));
        services.Configure<ExchangeSettings>(configuration.GetSection("Exchange"));
        services.Configure<LoggingSettings>(configuration.GetSection("Logging"));
        services.Configure<SecuritySettings>(configuration.GetSection("Security"));

        // Register DbContext
        services.AddDbContext<TradingBotDbContext>(options =>
        {
            options.UseNpgsql(settings.Database.ConnectionString);
        });

        // Register Repositories and Unit Of Work
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ISignalRepository, SignalRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ITradeRepository, TradeRepository>();

        // Register Health Checks
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("Database")
            .AddCheck<ExchangeHealthCheck>("Exchange");

        return services;
    }
}
