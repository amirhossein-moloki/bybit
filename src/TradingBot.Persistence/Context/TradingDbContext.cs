using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence.Context;

public class TradingDbContext : DbContext
{
    public DbSet<ExchangeAccount> ExchangeAccounts { get; set; } = null!;
    public DbSet<Symbol> Symbols { get; set; } = null!;
    public DbSet<Signal> Signals { get; set; } = null!;
    public DbSet<Order> Orders { get; set; } = null!;
    public DbSet<OrderEvent> OrderEvents { get; set; } = null!;
    public DbSet<Position> Positions { get; set; } = null!;
    public DbSet<StopLossHistory> StopLossHistories { get; set; } = null!;
    public DbSet<Trade> Trades { get; set; } = null!;
    public DbSet<RiskRule> RiskRules { get; set; } = null!;
    public DbSet<SystemLog> SystemLogs { get; set; } = null!;
    public DbSet<ParserTemplates> ParserTemplates { get; set; } = null!;
    public DbSet<TradingBot.Domain.RiskManagement.Entities.RiskEvaluation> RiskEvaluations { get; set; } = null!;
    public DbSet<TradingBot.Domain.RiskManagement.Entities.RiskProfile> RiskProfiles { get; set; } = null!;
    public DbSet<TradingBot.Domain.RiskManagement.Entities.TradeDecision> TradeDecisions { get; set; } = null!;

    public TradingDbContext(DbContextOptions<TradingDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TradingDbContext).Assembly);
    }

    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries();
        var utcNow = DateTime.UtcNow;

        foreach (var entry in entries)
        {
            if (entry.State == EntityState.Added)
            {
                var createdAtProp = entry.Metadata.FindProperty("CreatedAt");
                if (createdAtProp != null)
                {
                    entry.Property("CreatedAt").CurrentValue = utcNow;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                var updatedAtProp = entry.Metadata.FindProperty("UpdatedAt");
                if (updatedAtProp != null)
                {
                    entry.Property("UpdatedAt").CurrentValue = utcNow;
                }
            }
        }
    }
}
