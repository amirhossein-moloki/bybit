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

        if (context.ChangeTracker.HasChanges())
        {
            await context.SaveChangesAsync();
            logger?.LogInformation("Seed data committed successfully.");
        }
    }
}
