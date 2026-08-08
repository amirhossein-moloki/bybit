using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence.Configurations;

public class PositionTargetConfiguration : IEntityTypeConfiguration<PositionTarget>
{
    public void Configure(EntityTypeBuilder<PositionTarget> builder)
    {
        builder.ToTable("PositionTargets", t =>
        {
            t.HasCheckConstraint("CK_PositionTargets_Price", "\"Price\" > 0");
            t.HasCheckConstraint("CK_PositionTargets_Quantity", "\"Quantity\" > 0");
            t.HasCheckConstraint("CK_PositionTargets_Percentage", "\"Percentage\" > 0 AND \"Percentage\" <= 100");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PositionId)
            .IsRequired();

        builder.Property(x => x.TargetNumber)
            .IsRequired();

        builder.Property(x => x.Price)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.Quantity)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.Percentage)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.ExchangeOrderId)
            .HasMaxLength(100);

        builder.Property(x => x.ExecutedQuantity)
            .HasColumnType("numeric(18,8)");

        builder.Property(x => x.ExecutedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        // Indexes
        builder.HasIndex(x => x.PositionId);
        builder.HasIndex(x => new { x.PositionId, x.TargetNumber }).IsUnique();
    }
}
