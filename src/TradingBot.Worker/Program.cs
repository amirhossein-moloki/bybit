using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using TradingBot.Infrastructure.Configuration;
using TradingBot.Infrastructure.Logging;
using TradingBot.Persistence.Context;
using TradingBot.Worker;

try
{
    var builder = WebApplication.CreateBuilder(args);

    // 1. Centralized Configuration
    var settings = new TradingBotSettings();
    builder.Configuration.Bind(settings);

    // Override from explicit production environment variables if present
    var envApiKey = Environment.GetEnvironmentVariable("BYBIT_API_KEY");
    if (!string.IsNullOrEmpty(envApiKey))
    {
        settings.Exchange.ApiKey = envApiKey;
    }
    var envApiSecret = Environment.GetEnvironmentVariable("BYBIT_SECRET_KEY");
    if (!string.IsNullOrEmpty(envApiSecret))
    {
        settings.Exchange.ApiSecret = envApiSecret;
    }
    var envDbConn = Environment.GetEnvironmentVariable("DATABASE_CONNECTION");
    if (!string.IsNullOrEmpty(envDbConn))
    {
        settings.Database.ConnectionString = envDbConn;
    }

    // 2. Logging Infrastructure (Serilog)
    Log.Logger = SerilogConfiguration.CreateLoggerConfiguration(settings)
        .CreateLogger();

    builder.Host.UseSerilog((context, services, configuration) =>
    {
        configuration.ReadFrom.Configuration(context.Configuration);
    });

    Log.Information("Starting TradingBot.Worker...");

    // 3. Service Registrations
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddBybitExchange(options =>
    {
        options.ApiKey = settings.Exchange.ApiKey;
        options.ApiSecret = settings.Exchange.ApiSecret;
        options.UseSandbox = settings.Exchange.UseSandbox;
    });

    // 4. Background Hosted Services
    builder.Services.AddHostedService<ConnectionMonitorService>();
    builder.Services.AddHostedService<MarketDataBackgroundService>();
    builder.Services.AddHostedService<OrderSyncBackgroundService>();

    var app = builder.Build();

    // 5. Apply Migrations and Seed Database (Development/Production)
    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        try
        {
            var context = services.GetRequiredService<TradingDbContext>();
            if (context.Database.IsRelational())
            {
                Log.Information("Applying pending migrations...");
                await context.Database.MigrateAsync();
                Log.Information("Migrations applied successfully.");
            }
            else
            {
                Log.Information("Database is not relational. Ensuring database created...");
                await context.Database.EnsureCreatedAsync();
            }

            Log.Information("Seeding database...");
            var logger = services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<TradingDbContext>>();
            await DatabaseSeeder.SeedAsync(context, logger);
            Log.Information("Database seeding completed.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "An error occurred while migrating or seeding the database.");
        }
    }

    // 6. Health Monitoring Foundation
    app.MapHealthChecks("/health");

    // Root status check
    app.MapGet("/", () => new
    {
        Name = "Telegram Signal Trading Bot API Host",
        Status = "Online",
        Timestamp = DateTime.UtcNow
    });

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "TradingBot.Worker terminated unexpectedly.");
}
finally
{
    Log.CloseAndFlush();
}

namespace TradingBot.Worker
{
    // Make the implicit Program class public so that integration tests can reference it via WebApplicationFactory
    public partial class Program { }
}
