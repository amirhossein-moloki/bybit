using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using TradingBot.Infrastructure.Configuration;
using TradingBot.Infrastructure.Logging;
using TradingBot.Worker;

try
{
    var builder = WebApplication.CreateBuilder(args);

    // 1. Centralized Configuration
    var settings = new TradingBotSettings();
    builder.Configuration.Bind(settings);

    // 2. Logging Infrastructure (Serilog)
    Log.Logger = SerilogConfiguration.CreateLoggerConfiguration(settings)
        .CreateBootstrapLogger();

    builder.Host.UseSerilog((context, services, configuration) =>
    {
        configuration.ReadFrom.Configuration(context.Configuration);
    });

    Log.Information("Starting TradingBot.Worker...");

    // 3. Service Registrations
    builder.Services.AddApplication();
    builder.Services.AddInfrastructure(builder.Configuration);
    builder.Services.AddBybitExchange();

    // 4. Background Hosted Service
    builder.Services.AddHostedService<TradingBotWorkerService>();

    var app = builder.Build();

    // 5. Health Monitoring Foundation
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
