using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TradingBot.Application.Interfaces;
using TradingBot.Application.Repositories;
using TradingBot.Infrastructure.Configuration;
using TradingBot.Infrastructure.Security;
using TradingBot.Persistence.Repositories;
using TradingBot.Persistence.UnitOfWork;
using TradingBot.Infrastructure.Health;
using TradingBot.Infrastructure.Resilience;
using TradingBot.Persistence;
using TradingBot.Persistence.Context;

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

        // Register DbContext (delegated to Persistence layer registration)
        services.AddPersistence(configuration);

        // Register Encryption Service
        services.AddSingleton<IEncryptionService, EncryptionService>();

        // Register Resilience Service
        services.AddSingleton<IResilienceService, ResilienceService>();

        // Register Repositories and Unit Of Work
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<ISignalRepository, SignalRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<ITradeRepository, TradeRepository>();
        services.AddScoped<IPositionRepository, PositionRepository>();
        services.AddScoped<IExchangeAccountRepository, ExchangeAccountRepository>();
        services.AddScoped<ISystemLogRepository, SystemLogRepository>();
        services.AddScoped<IRiskEvaluationRepository, RiskEvaluationRepository>();
        services.AddScoped<IRiskProfileRepository, RiskProfileRepository>();
        services.AddScoped<ITradeDecisionRepository, TradeDecisionRepository>();
        services.AddScoped<TradingBot.Domain.Repositories.IParserTemplateRepository, ParserTemplateRepository>();
        services.AddScoped<IRepository<TradingBot.Domain.Entities.Symbol>, SymbolRepository>();

        // Register Health Checks
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("Database")
            .AddCheck<ExchangeHealthCheck>("Exchange")
            .AddCheck<ExchangeConnectionHealthCheck>("ExchangeConnection")
            .AddCheck<WebSocketHealthCheck>("WebSocket")
            .AddCheck<TradingEngineHealthCheck>("TradingEngine");

        return services;
    }
}
