using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence.Configurations;

public class PositionEventConfiguration : IEntityTypeConfiguration<PositionEvent>
{
    public void Configure(EntityTypeBuilder<PositionEvent> builder)
    {
        builder.ToTable("PositionEvents");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PositionId)
            .IsRequired();

        builder.Property(x => x.EventType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Payload)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        // Indexes
        builder.HasIndex(x => x.PositionId);
        builder.HasIndex(x => x.EventType);
    }
}
