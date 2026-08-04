using System;
using Serilog;
using Serilog.Events;
using TradingBot.Infrastructure.Configuration;

namespace TradingBot.Infrastructure.Logging;

public static class SerilogConfiguration
{
    public static LoggerConfiguration CreateLoggerConfiguration(TradingBotSettings settings)
    {
        var logLevel = LogEventLevel.Information;
        if (Enum.TryParse<LogEventLevel>(settings.Logging.LogLevel, true, out var parsedLevel))
        {
            logLevel = parsedLevel;
        }

        var config = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext();

        if (settings.Logging.EnableConsole)
        {
            config = config.WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
            );
        }

        // We can also configure file logging if path is provided
        if (!string.IsNullOrWhiteSpace(settings.Logging.LogFilePath))
        {
            // Just configure the console and general logger setup.
            // If file logger was needed, we could add file sink.
        }

        return config;
    }
}
