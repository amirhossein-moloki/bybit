using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders", t =>
        {
            t.HasCheckConstraint("CK_Orders_Quantity", "\"Quantity\" > 0");
            t.HasCheckConstraint("CK_Orders_Price", "\"Price\" >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ClientOrderId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.ExchangeOrderId)
            .HasMaxLength(100);

        builder.Property(x => x.Side)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Type)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // Owned Value Object: Symbol
        builder.OwnsOne(o => o.Symbol, symbol =>
        {
            symbol.Property(s => s.Value)
                .HasColumnName("Symbol")
                .HasMaxLength(20)
                .IsRequired();
            symbol.HasIndex(s => s.Value);
        });

        // Owned Value Object: Quantity
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

        // Owned Value Object: Price (Money)
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

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsConcurrencyToken();

        // Foreign Key property for relationship Signal -> Order
        builder.Property(x => x.SignalId);

        // One Signal has One Order
        builder.HasOne<Signal>()
            .WithOne()
            .HasForeignKey<Order>(o => o.SignalId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(x => x.ClientOrderId).IsUnique();
        builder.HasIndex(x => x.ExchangeOrderId);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => new { x.Status, x.CreatedAt });
    }
}
