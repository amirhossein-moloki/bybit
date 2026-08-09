using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using TradingBot.Application.Interfaces;
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

    // Configure Authentication and Authorization for Dashboard API
    builder.Services.AddAuthentication("DashboardToken")
        .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, DashboardAuthHandler>("DashboardToken", null);
    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("DashboardRead", policy =>
            policy.RequireClaim("Permission", "dashboard.read"));
    });

    // 3. Service Registrations
    builder.Services.AddApplication(builder.Configuration);
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddParser(builder.Configuration);
    builder.Services.AddRiskManagement(builder.Configuration);
    builder.Services.AddBybitExchange(options =>
    {
        options.ApiKey = settings.Exchange.ApiKey;
        options.ApiSecret = settings.Exchange.ApiSecret;
        options.UseSandbox = settings.Exchange.UseSandbox;
        options.Environment = settings.Exchange.Environment;
        options.RecvWindow = settings.Exchange.RecvWindow;
    });
    builder.Services.AddTelegramIntegration(builder.Configuration);

    // 4. Background Hosted Services
    builder.Services.AddHostedService<ConnectionMonitorService>();
    builder.Services.AddHostedService<MarketDataBackgroundService>();
    builder.Services.AddHostedService<OrderSyncBackgroundService>();
    builder.Services.AddHostedService<PositionSyncBackgroundService>();
    builder.Services.AddHostedService<SignalStorageWorker>();
    builder.Services.AddHostedService<OrderReconciliationWorker>();
    builder.Services.AddHostedService<MonitoringWorker>();
    builder.Services.AddHostedService<MonitoringEventProcessor>();
    builder.Services.AddHostedService<AlertEvaluationWorker>();
    builder.Services.AddHostedService<NotificationWorker>();

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
                if (context.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
                {
                    Log.Information("SQLite detected. Ensuring database created...");
                    await context.Database.EnsureCreatedAsync();
                }
                else
                {
                    Log.Information("Applying pending migrations...");
                    await context.Database.MigrateAsync();
                    Log.Information("Migrations applied successfully.");
                }
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

            // Startup Position Recovery Flow
            try
            {
                Log.Information("Starting startup position recovery flow...");
                var recoveryService = services.GetRequiredService<IPositionRecoveryService>();
                await recoveryService.RecoverPositionsAsync();
                Log.Information("Startup position recovery flow completed successfully.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An error occurred during startup position recovery flow.");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "An error occurred while migrating or seeding the database.");
        }
    }

    // 6. Health Monitoring Foundation
    app.MapHealthChecks("/health");

    // Detailed health status provider check
    app.MapGet("/monitoring/health", (TradingBot.Application.Monitoring.IHealthStatusProvider provider) =>
    {
        var overall = provider.GetOverallStatus().ToString();
        var components = new System.Collections.Generic.Dictionary<string, string>();
        foreach (var status in provider.GetComponentStatuses())
        {
            components[status.Key] = status.Value.Status.ToString();
        }

        return Results.Ok(new
        {
            status = overall,
            timestamp = DateTime.UtcNow,
            components = components
        });
    });

    app.MapGet("/health/status", (TradingBot.Application.Monitoring.IHealthStatusProvider provider) =>
    {
        var overall = provider.GetOverallStatus().ToString();
        var components = new System.Collections.Generic.Dictionary<string, string>();
        foreach (var status in provider.GetComponentStatuses())
        {
            components[status.Key] = status.Value.Status.ToString();
        }

        return Results.Ok(new
        {
            status = overall,
            timestamp = DateTime.UtcNow,
            components = components
        });
    });

    // Root status check
    app.MapGet("/", () => new
    {
        Name = "Telegram Signal Trading Bot API Host",
        Status = "Online",
        Creator = "Amir",
        Timestamp = DateTime.UtcNow
    });

    // Map Dashboard Endpoints
    app.MapDashboardEndpoints();

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
