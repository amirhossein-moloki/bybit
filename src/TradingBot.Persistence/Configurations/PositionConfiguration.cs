using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence.Configurations;

public class PositionConfiguration : IEntityTypeConfiguration<Position>
{
    public void Configure(EntityTypeBuilder<Position> builder)
    {
        builder.ToTable("Positions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.OrderId)
            .IsRequired();

        builder.Property(x => x.Symbol)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Side)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.EntryPrice)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.Quantity)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.StopLoss)
            .HasColumnType("numeric(18,8)");

        builder.Property(x => x.TakeProfit)
            .HasColumnType("numeric(18,8)");

        builder.Property(x => x.CurrentPrice)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.UnrealizedPnL)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.OpenedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.Property(x => x.ClosedAt)
            .HasColumnType("timestamp with time zone");

        // Shadow properties for CreatedAt/UpdatedAt
        builder.Property<DateTime>("CreatedAt")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.Property<DateTime?>("UpdatedAt")
            .HasColumnType("timestamp with time zone");

        // One Order has One Position
        builder.HasOne<Order>()
            .WithOne()
            .HasForeignKey<Position>(p => p.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(x => new { x.Symbol, x.Status });
    }
}
