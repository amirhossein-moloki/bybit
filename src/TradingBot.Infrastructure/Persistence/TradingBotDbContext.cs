using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.Entities;

namespace TradingBot.Infrastructure.Persistence;

public class TradingBotDbContext : DbContext
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Trade> Trades => Set<Trade>();
    public DbSet<Signal> Signals => Set<Signal>();

    public TradingBotDbContext(DbContextOptions<TradingBotDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Order Configuration
        modelBuilder.Entity<Order>(builder =>
        {
            builder.ToTable("Orders");
            builder.HasKey(o => o.Id);

            builder.Property(o => o.ClientOrderId)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(o => o.ExchangeOrderId)
                .HasMaxLength(100);

            // Map Value Objects
            builder.OwnsOne(o => o.Symbol, symbol =>
            {
                symbol.Property(s => s.Value)
                    .HasColumnName("Symbol")
                    .HasMaxLength(20)
                    .IsRequired();
            });

            builder.Property(o => o.Side)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            builder.Property(o => o.Type)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            builder.OwnsOne(o => o.Quantity, qty =>
            {
                qty.Property(q => q.Value)
                    .HasColumnName("Quantity")
                    .HasColumnType("numeric(18,8)")
                    .IsRequired();
                qty.Property(q => q.Unit)
                    .HasColumnName("QuantityUnit")
                    .HasMaxLength(10)
                    .IsRequired();
            });

            builder.OwnsOne(o => o.Price, price =>
            {
                price.Property(p => p.Amount)
                    .HasColumnName("Price")
                    .HasColumnType("numeric(18,8)")
                    .IsRequired();
                price.Property(p => p.Currency)
                    .HasColumnName("PriceCurrency")
                    .HasMaxLength(10)
                    .IsRequired();
            });

            builder.Property(o => o.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            builder.Property(o => o.CreatedAt)
                .IsRequired();

            builder.Property(o => o.UpdatedAt);
        });

        // Trade / TradeHistory Configuration
        modelBuilder.Entity<Trade>(builder =>
        {
            builder.ToTable("TradeHistory");
            builder.HasKey(t => t.Id);

            builder.Property(t => t.TradeId)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(t => t.OrderId)
                .HasMaxLength(100)
                .IsRequired();

            builder.Property(t => t.Symbol)
                .HasMaxLength(20)
                .IsRequired();

            builder.Property(t => t.Side)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            builder.Property(t => t.Price)
                .HasColumnName("ExecutionPrice")
                .HasColumnType("numeric(18,8)")
                .IsRequired();

            builder.Property(t => t.Quantity)
                .HasColumnName("ExecutionQuantity")
                .HasColumnType("numeric(18,8)")
                .IsRequired();

            builder.Property(t => t.Fee)
                .HasColumnType("numeric(18,8)")
                .IsRequired();

            builder.Property(t => t.FeeAsset)
                .HasMaxLength(10)
                .IsRequired();

            builder.Property(t => t.ExecutedAt)
                .IsRequired();
        });

        // Signal Configuration
        modelBuilder.Entity<Signal>(builder =>
        {
            builder.ToTable("Signals");
            builder.HasKey(s => s.Id);

            builder.Property(s => s.Symbol)
                .HasMaxLength(20)
                .IsRequired();

            builder.Property(s => s.Type)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            builder.Property(s => s.Price)
                .HasColumnType("numeric(18,8)")
                .IsRequired();

            builder.Property(s => s.Quantity)
                .HasColumnType("numeric(18,8)")
                .IsRequired();

            builder.Property(s => s.CreatedAt)
                .IsRequired();
        });
    }
}
