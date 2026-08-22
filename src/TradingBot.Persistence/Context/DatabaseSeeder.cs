using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence.Context;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(TradingDbContext context, ILogger? logger = null)
    {
        logger?.LogInformation("Checking database seed requirements...");

        // Seed Symbols
        if (!await context.Symbols.AnyAsync())
        {
            logger?.LogInformation("Seeding default Symbols...");

            var symbols = new[]
            {
                new Symbol("BYBIT", "BTCUSDT", "BTC", "USDT", 0.1m, 0.001m, 0.0001m),
                new Symbol("BYBIT", "ETHUSDT", "ETH", "USDT", 0.01m, 0.01m, 0.001m)
            };

            await context.Symbols.AddRangeAsync(symbols);
            logger?.LogInformation("Successfully seeded 2 default symbols (BTCUSDT, ETHUSDT).");
        }
        else
        {
            logger?.LogInformation("Symbols table is not empty. Skipping symbol seeding.");
        }

        // Seed Risk Rules
        if (!await context.RiskRules.AnyAsync())
        {
            logger?.LogInformation("Seeding default Risk Rules...");

            var defaultRiskRule = new RiskRule(
                maxRiskPercent: 2.0m,      // 2% max risk per trade
                maxOpenPositions: 5,       // 5 max open positions
                maxDailyLoss: 1000m,       // $1000 max daily loss
                maxLeverage: 10            // 10x max leverage
            );

            await context.RiskRules.AddAsync(defaultRiskRule);
            logger?.LogInformation("Successfully seeded default Risk Configuration.");
        }
        else
        {
            logger?.LogInformation("RiskRules table is not empty. Skipping risk rules seeding.");
        }

        // Seed ParserTemplates
        if (!await context.ParserTemplates.AnyAsync())
        {
            logger?.LogInformation("Seeding default ParserTemplates...");

            var channelARules = @"[
                {""Field"":""Symbol"",""Pattern"":"""",""Extractor"":""SymbolExtractor"",""Required"":true,""Order"":1},
                {""Field"":""Side"",""Pattern"":"""",""Extractor"":""DirectionExtractor"",""Required"":true,""Order"":2},
                {""Field"":""EntryPrice"",""Pattern"":""Entry:"",""Extractor"":""EntryExtractor"",""Required"":true,""Order"":3},
                {""Field"":""StopLoss"",""Pattern"":""SL:"",""Extractor"":""StopLossExtractor"",""Required"":true,""Order"":4},
                {""Field"":""TakeProfits"",""Pattern"":""TP"",""Extractor"":""TakeProfitExtractor"",""Required"":true,""Order"":5}
            ]";

            var channelBRules = @"[
                {""Field"":""Symbol"",""Pattern"":"""",""Extractor"":""SymbolExtractor"",""Required"":true,""Order"":1},
                {""Field"":""Side"",""Pattern"":"""",""Extractor"":""DirectionExtractor"",""Required"":true,""Order"":2},
                {""Field"":""EntryPrice"",""Pattern"":""BUY AREA"",""Extractor"":""EntryExtractor"",""Required"":true,""Order"":3},
                {""Field"":""StopLoss"",""Pattern"":""STOP"",""Extractor"":""StopLossExtractor"",""Required"":true,""Order"":4},
                {""Field"":""TakeProfits"",""Pattern"":""TARGET"",""Extractor"":""TakeProfitExtractor"",""Required"":true,""Order"":5}
            ]";

            var templates = new[]
            {
                new ParserTemplates
                {
                    Id = Guid.NewGuid(),
                    Name = "Crypto VIP Template",
                    ChannelId = 12345,
                    Enabled = true,
                    ConfigurationJson = channelARules,
                    CreatedAt = DateTime.UtcNow
                },
                new ParserTemplates
                {
                    Id = Guid.NewGuid(),
                    Name = "Channel B Template",
                    ChannelId = 67890,
                    Enabled = true,
                    ConfigurationJson = channelBRules,
                    CreatedAt = DateTime.UtcNow
                }
            };

            await context.ParserTemplates.AddRangeAsync(templates);
            logger?.LogInformation("Successfully seeded 2 default parser templates.");
        }
        else
        {
            logger?.LogInformation("ParserTemplates table is not empty. Skipping templates seeding.");
        }

        // Seed/Migrate Legacy Telegram Channels from Environment Variables
        var envChannels = Environment.GetEnvironmentVariable("Telegram__Channels")
                       ?? Environment.GetEnvironmentVariable("TELEGRAM_CHANNELS");

        if (!string.IsNullOrWhiteSpace(envChannels))
        {
            logger?.LogInformation("Found legacy Telegram__Channels in environment. Migrating to TelegramSources table...");
            var splitChannels = envChannels.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawCh in splitChannels)
            {
                var trimmed = rawCh.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                long chatId = 0;
                string title = trimmed;
                string? username = null;

                if (long.TryParse(trimmed, out var parsedId))
                {
                    chatId = parsedId;
                    title = $"Legacy Channel ({parsedId})";
                }
                else
                {
                    // Generate stable hash ID for named channel string
                    chatId = Math.Abs(trimmed.GetHashCode());
                    username = trimmed.StartsWith("@") ? trimmed : "@" + trimmed;
                }

                bool exists = await context.TelegramSources.AnyAsync(s => s.TelegramChatId == chatId);
                if (!exists)
                {
                    var legacySource = new TelegramSource(
                        chatId,
                        title,
                        username,
                        TradingBot.Domain.Enums.TelegramSourceType.Channel,
                        isEnabled: true,
                        listenForSignals: true,
                        processMessages: true
                    );
                    await context.TelegramSources.AddAsync(legacySource);
                    logger?.LogInformation("Migrated legacy channel '{Channel}' to TelegramSource entity in database.", trimmed);
                }
            }
        }

        if (context.ChangeTracker.HasChanges())
        {
            await context.SaveChangesAsync();
            logger?.LogInformation("Seed data committed successfully.");
        }
    }
}
