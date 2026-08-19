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
    var envBybitEnv = Environment.GetEnvironmentVariable("BYBIT_ENVIRONMENT");
    if (!string.IsNullOrEmpty(envBybitEnv))
    {
        settings.Exchange.Environment = envBybitEnv;
    }
    var envDemoApiKey = Environment.GetEnvironmentVariable("BYBIT_DEMO_API_KEY");
    if (!string.IsNullOrEmpty(envDemoApiKey))
    {
        settings.Exchange.DemoApiKey = envDemoApiKey;
    }
    var envDemoApiSecret = Environment.GetEnvironmentVariable("BYBIT_DEMO_API_SECRET");
    if (!string.IsNullOrEmpty(envDemoApiSecret))
    {
        settings.Exchange.DemoApiSecret = envDemoApiSecret;
    }
    var envMainnetApiKey = Environment.GetEnvironmentVariable("BYBIT_MAINNET_API_KEY");
    if (!string.IsNullOrEmpty(envMainnetApiKey))
    {
        settings.Exchange.MainnetApiKey = envMainnetApiKey;
    }
    var envMainnetApiSecret = Environment.GetEnvironmentVariable("BYBIT_MAINNET_API_SECRET");
    if (!string.IsNullOrEmpty(envMainnetApiSecret))
    {
        settings.Exchange.MainnetApiSecret = envMainnetApiSecret;
    }
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
    var envProxyUrl = Environment.GetEnvironmentVariable("BYBIT_PROXY_URL");
    if (!string.IsNullOrEmpty(envProxyUrl))
    {
        settings.Exchange.ProxyUrl = envProxyUrl;
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
    builder.Services.AddScoped<IStartupRecoveryManager, TradingBot.Worker.Lifecycle.StartupRecoveryManager>();
    builder.Services.AddSingleton<IGracefulShutdownManager, TradingBot.Worker.Lifecycle.GracefulShutdownManager>();
    builder.Services.AddParser(builder.Configuration);
    builder.Services.AddRiskManagement(builder.Configuration);
    builder.Services.AddBybitExchange(options =>
    {
        options.ApiKey = settings.Exchange.ApiKey;
        options.ApiSecret = settings.Exchange.ApiSecret;
        options.DemoApiKey = settings.Exchange.DemoApiKey;
        options.DemoApiSecret = settings.Exchange.DemoApiSecret;
        options.MainnetApiKey = settings.Exchange.MainnetApiKey;
        options.MainnetApiSecret = settings.Exchange.MainnetApiSecret;
        options.UseSandbox = settings.Exchange.UseSandbox;
        options.Environment = settings.Exchange.Environment;
        options.RecvWindow = settings.Exchange.RecvWindow;
        options.ProxyUrl = settings.Exchange.ProxyUrl;
    });
    builder.Services.AddTelegramIntegration(builder.Configuration);

    // 4. Background Hosted Services
    builder.Services.AddHostedService<ConnectionMonitorService>();
    builder.Services.AddHostedService<MarketDataBackgroundService>();
    builder.Services.AddHostedService<OrderSyncBackgroundService>();
    builder.Services.AddHostedService<PositionSyncBackgroundService>();
    builder.Services.AddHostedService<SignalStorageWorker>();
    builder.Services.AddHostedService<OrderReconciliationWorker>();
    builder.Services.AddHostedService<IncompleteOperationRecoveryWorker>();
    builder.Services.AddHostedService<MonitoringWorker>();
    builder.Services.AddHostedService<MonitoringEventProcessor>();
    builder.Services.AddHostedService<AlertEvaluationWorker>();
    builder.Services.AddHostedService<NotificationWorker>();

    var app = builder.Build();

    // Check for 'doctor' CLI command before starting the runtime
    if (args.Length > 0 && (args[0].Equals("doctor", StringComparison.OrdinalIgnoreCase) || args[0].Equals("--doctor", StringComparison.OrdinalIgnoreCase)))
    {
        await TradingBotDoctor.RunDiagnosticsAsync(app.Services);
        return;
    }

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

            // Startup Recovery, State Reconciliation & Readiness sequence
            try
            {
                Log.Information("Starting unified startup recovery and state reconciliation sequence...");
                var startupRecoveryManager = services.GetRequiredService<IStartupRecoveryManager>();
                await startupRecoveryManager.RunRecoverySequenceAsync();
                Log.Information("Unified startup recovery and state reconciliation sequence completed successfully.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An error occurred during unified startup recovery and state reconciliation sequence.");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "An error occurred while migrating or seeding the database.");
        }
    }

    // Instantiate GracefulShutdownManager so its shutdown callback is registered with IHostApplicationLifetime
    app.Services.GetRequiredService<IGracefulShutdownManager>();

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

    // Map Analytics Endpoints
    app.MapAnalyticsEndpoints();

    // Map Telegram Endpoints
    app.MapTelegramEndpoints();

    app.Run();
}
catch (Exception ex)
{
    Console.WriteLine("FATAL_STARTUP_EXCEPTION: " + ex.ToString());
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
