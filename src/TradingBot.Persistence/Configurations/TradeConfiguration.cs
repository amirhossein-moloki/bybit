using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence.Configurations;

public class TradeConfiguration : IEntityTypeConfiguration<Trade>
{
    public void Configure(EntityTypeBuilder<Trade> builder)
    {
        builder.ToTable("Trades");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TradeId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.OrderId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Symbol)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Side)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Price)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.Quantity)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.Fee)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.FeeAsset)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(x => x.ExecutedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // New fields
        builder.Property(x => x.PositionId);

        builder.Property(x => x.EntryPrice)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.ExitPrice)
            .HasColumnType("numeric(18,8)");

        builder.Property(x => x.ProfitLoss)
            .HasColumnType("numeric(18,8)");

        builder.Property(x => x.ClosedAt)
            .HasColumnType("timestamp with time zone");

        // Shadow properties for CreatedAt/UpdatedAt
        builder.Property<DateTime>("CreatedAt")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.Property<DateTime?>("UpdatedAt")
            .HasColumnType("timestamp with time zone");

        // One Position has One Trade
        builder.HasOne<Position>()
            .WithOne()
            .HasForeignKey<Trade>(t => t.PositionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(x => x.PositionId);
        builder.HasIndex(x => x.TradeId);
    }
}
