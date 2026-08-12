using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TradingBot.Application.Interfaces;
using TradingBot.Persistence.Context;
using TradingBot.Telegram.Configuration;

namespace TradingBot.Worker;

public static class TradingBotDoctor
{
    public static async Task RunDiagnosticsAsync(IServiceProvider services)
    {
        Console.WriteLine("====================================================================");
        Console.WriteLine("                  Trading Bot Diagnostic Report                     ");
        Console.WriteLine("====================================================================\n");

        using var scope = services.CreateScope();
        var scopedServices = scope.ServiceProvider;

        var recommendations = new List<string>();

        // 1. Application Check
        Console.WriteLine("Application:");
        try
        {
            var config = scopedServices.GetRequiredService<IConfiguration>();
            var env = config["Application:Environment"] ?? config["ASPNETCORE_ENVIRONMENT"] ?? "Unknown";
            var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0";
            var botName = config["Application:BotName"] ?? "TelegramSignalTradingBot";

            Console.WriteLine("  Status: OK");
            Console.WriteLine($"  Version: {version}");
            Console.WriteLine($"  Environment: {env}");
            Console.WriteLine($"  Bot Name: {botName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine("  Status: FAILED");
            Console.WriteLine($"  Reason: {ex.Message}");
            recommendations.Add("Check application appsettings.json/environment variable bindings.");
        }
        Console.WriteLine();

        // 2. Database Check
        Console.WriteLine("Database:");
        try
        {
            var dbContext = scopedServices.GetRequiredService<TradingDbContext>();
            var canConnect = await dbContext.Database.CanConnectAsync();

            if (!canConnect)
            {
                Console.WriteLine("  Status: FAILED");
                Console.WriteLine("  Reason: Cannot connect to database.");
                recommendations.Add("Check database container status and DATABASE_CONNECTION connection string.");
            }
            else
            {
                var sw = Stopwatch.StartNew();
                // Measure query latency using a simple query
                await dbContext.Database.ExecuteSqlRawAsync("SELECT 1;");
                sw.Stop();

                var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();
                var pendingCount = pendingMigrations.Count();

                // Check key tables
                bool tablesExist = true;
                string missingTableReason = "";
                try
                {
                    // Check if we can select from main tables
                    await dbContext.Database.ExecuteSqlRawAsync("SELECT COUNT(*) FROM \"TelegramMessages\";");
                }
                catch (Exception ex)
                {
                    tablesExist = false;
                    missingTableReason = ex.Message;
                }

                if (pendingCount > 0)
                {
                    Console.WriteLine("  Status: WARNING");
                    Console.WriteLine("  Reason: Pending migration");
                    recommendations.Add("Run database migration using 'dotnet ef database update'.");
                }
                else if (!tablesExist)
                {
                    Console.WriteLine("  Status: WARNING");
                    Console.WriteLine($"  Reason: Required tables are missing ({missingTableReason})");
                    recommendations.Add("Run database migration to initialize system tables.");
                }
                else
                {
                    Console.WriteLine("  Status: OK");
                }

                Console.WriteLine($"  Query Latency: {sw.ElapsedMilliseconds} ms");
                Console.WriteLine($"  Pending Migrations: {pendingCount}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("  Status: FAILED");
            Console.WriteLine($"  Reason: {ex.Message}");
            recommendations.Add("Ensure PostgreSQL container is running and credentials are valid.");
        }
        Console.WriteLine();

        // 3. Redis Check
        Console.WriteLine("Redis:");
        try
        {
            var config = scopedServices.GetRequiredService<IConfiguration>();
            var host = config["REDIS_HOST"] ?? "localhost";
            var portStr = config["REDIS_PORT"] ?? "6379";
            int.TryParse(portStr, out var port);
            if (port <= 0) port = 6379;

            var sw = Stopwatch.StartNew();
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);

            // Timeout after 3 seconds
            if (await Task.WhenAny(connectTask, Task.Delay(3000)) == connectTask)
            {
                await connectTask; // Propagate any connection exceptions
                sw.Stop();
                Console.WriteLine("  Status: OK");
                Console.WriteLine($"  Response Latency: {sw.ElapsedMilliseconds} ms");
                Console.WriteLine($"  Host: {host}:{port}");
            }
            else
            {
                Console.WriteLine("  Status: FAILED");
                Console.WriteLine("  Reason: Connection timed out.");
                recommendations.Add("Ensure Redis service is started on port 6379.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("  Status: FAILED");
            Console.WriteLine($"  Reason: {ex.Message}");
            recommendations.Add("Check Redis server status and firewall configuration.");
        }
        Console.WriteLine();

        // 4. Bybit Check
        Console.WriteLine("Bybit:");
        try
        {
            var exchangeClient = scopedServices.GetRequiredService<IExchangeClient>();
            var config = scopedServices.GetRequiredService<IConfiguration>();
            var apiKey = config["Exchange:ApiKey"] ?? config["BYBIT_API_KEY"] ?? "";
            var apiSecret = config["Exchange:ApiSecret"] ?? config["BYBIT_SECRET_KEY"] ?? "";
            var useSandbox = config.GetValue<bool>("Exchange:UseSandbox");

            var sw = Stopwatch.StartNew();
            var pingOk = await exchangeClient.PingAsync(CancellationToken.None);
            sw.Stop();

            if (!pingOk)
            {
                Console.WriteLine("  Status: FAILED");
                Console.WriteLine("  Reason: REST Connectivity ping failed.");
                recommendations.Add("Verify internet routing to Bybit REST V5 gateway.");
            }
            else
            {
                // Verify auth status if keys are configured
                if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
                {
                    Console.WriteLine("  Status: WARNING");
                    Console.WriteLine("  Reason: API Keys not configured.");
                    recommendations.Add("Set BYBIT_API_KEY and BYBIT_SECRET_KEY inside .env.");
                }
                else
                {
                    try
                    {
                        // Check balance to verify authentication
                        await exchangeClient.GetAccountBalanceAsync("USDT", CancellationToken.None);
                        Console.WriteLine("  Status: OK");
                    }
                    catch (Exception authEx) when (authEx.Message.Contains("Signature", StringComparison.OrdinalIgnoreCase) ||
                                                  authEx.Message.Contains("key", StringComparison.OrdinalIgnoreCase) ||
                                                  authEx.Message.Contains("auth", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("  Status: FAILED");
                        Console.WriteLine("  Reason: Authentication failed");
                        recommendations.Add("Update Bybit credentials with valid keys.");
                    }
                    catch (Exception ex)
                    {
                        // Some other network or API error
                        Console.WriteLine("  Status: WARNING");
                        Console.WriteLine($"  Reason: Auth Check failed ({ex.Message})");
                        recommendations.Add("Inspect Bybit API response or account setup permissions.");
                    }
                }

                Console.WriteLine($"  REST Latency: {sw.ElapsedMilliseconds} ms");
                Console.WriteLine($"  Endpoint Environment: {(useSandbox ? "Testnet/Sandbox" : "Production/Live")}");

                // WebSocket check
                var wsHost = useSandbox ? "stream-testnet.bybit.com" : "stream.bybit.com";
                try
                {
                    using var wsClient = new TcpClient();
                    var wsConnect = wsClient.ConnectAsync(wsHost, 443);
                    if (await Task.WhenAny(wsConnect, Task.Delay(3000)) == wsConnect)
                    {
                        await wsConnect;
                        Console.WriteLine("  WebSocket Connectivity: OK");
                    }
                    else
                    {
                        Console.WriteLine("  WebSocket Connectivity: FAILED (Timeout)");
                        recommendations.Add("Check network port 443 outbound firewall access.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  WebSocket Connectivity: FAILED ({ex.Message})");
                    recommendations.Add("Verify DNS resolution and outbound routing to stream.bybit.com.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("  Status: FAILED");
            Console.WriteLine($"  Reason: {ex.Message}");
            recommendations.Add("Ensure Bybit client registrations are correct.");
        }
        Console.WriteLine();

        // 5. Telegram Check
        Console.WriteLine("Telegram:");
        try
        {
            var telegramOptions = scopedServices.GetService<IOptions<TelegramOptions>>()?.Value;
            if (telegramOptions == null)
            {
                Console.WriteLine("  Status: WARNING");
                Console.WriteLine("  Reason: Telegram settings not registered.");
                recommendations.Add("Verify TelegramOptions configurations are correctly wired.");
            }
            else if (!telegramOptions.Enabled)
            {
                Console.WriteLine("  Status: OK (Disabled in configuration)");
            }
            else if (string.IsNullOrWhiteSpace(telegramOptions.ApiId) || string.IsNullOrWhiteSpace(telegramOptions.ApiHash))
            {
                Console.WriteLine("  Status: WARNING");
                Console.WriteLine("  Reason: ApiId or ApiHash is missing.");
                recommendations.Add("Add Telegram__ApiId and Telegram__ApiHash to .env settings.");
            }
            else
            {
                // Connectivity check to Telegram Server
                try
                {
                    using var tc = new TcpClient();
                    var tcConnect = tc.ConnectAsync("telegram.org", 443);
                    if (await Task.WhenAny(tcConnect, Task.Delay(3000)) == tcConnect)
                    {
                        await tcConnect;
                        Console.WriteLine("  Status: OK");
                        Console.WriteLine($"  PhoneNumber: {telegramOptions.PhoneNumber}");
                        Console.WriteLine($"  Monitored Channels: {string.Join(", ", telegramOptions.Channels)}");
                    }
                    else
                    {
                        Console.WriteLine("  Status: WARNING");
                        Console.WriteLine("  Reason: Connection to telegram.org timed out.");
                        recommendations.Add("Ensure outbound connection to Telegram network is allowed.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("  Status: WARNING");
                    Console.WriteLine($"  Reason: DNS or Network Connection failure ({ex.Message})");
                    recommendations.Add("Check DNS and network firewall for Telegram API routing.");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("  Status: FAILED");
            Console.WriteLine($"  Reason: {ex.Message}");
            recommendations.Add("Ensure Telegram client configurations are validated.");
        }
        Console.WriteLine();

        // 6. Configuration Check
        Console.WriteLine("Configuration:");
        try
        {
            var config = scopedServices.GetRequiredService<IConfiguration>();
            var encryptionKey = config["Security:EncryptionKey"] ?? config["Security__EncryptionKey"] ?? "";
            var useSandbox = config.GetValue<bool>("Exchange:UseSandbox");
            var apiKey = config["Exchange:ApiKey"] ?? config["BYBIT_API_KEY"] ?? "";

            var configWarnings = new List<string>();

            if (string.IsNullOrWhiteSpace(encryptionKey))
            {
                configWarnings.Add("Security__EncryptionKey is completely empty!");
                recommendations.Add("Configure a strong Security__EncryptionKey (32 characters) to protect database fields.");
            }
            else if (encryptionKey.Length < 16)
            {
                configWarnings.Add("Security__EncryptionKey is too short (less than 16 bytes) and insecure.");
                recommendations.Add("Update Security__EncryptionKey to be exactly 32 characters.");
            }

            if (!useSandbox && (apiKey.Contains("testnet", StringComparison.OrdinalIgnoreCase) || apiKey.Contains("demo", StringComparison.OrdinalIgnoreCase)))
            {
                configWarnings.Add("Running in Live Production environment but with Testnet API keys!");
                recommendations.Add("Ensure production BYBIT_API_KEY is supplied when sandbox mode is disabled.");
            }

            if (configWarnings.Count > 0)
            {
                Console.WriteLine("  Status: WARNING");
                foreach (var warning in configWarnings)
                {
                    Console.WriteLine($"  - {warning}");
                }
            }
            else
            {
                Console.WriteLine("  Status: OK (All validations passed)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("  Status: FAILED");
            Console.WriteLine($"  Reason: {ex.Message}");
        }
        Console.WriteLine();

        // Recommendations Output
        Console.WriteLine("Recommendations:");
        if (recommendations.Count > 0)
        {
            foreach (var rec in recommendations)
            {
                Console.WriteLine($"* {rec}");
            }
        }
        else
        {
            Console.WriteLine("None! Your Trading Bot is healthy and production ready.");
        }
        Console.WriteLine("====================================================================");
    }
}
