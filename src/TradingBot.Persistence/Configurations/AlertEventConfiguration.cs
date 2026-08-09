using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence.Configurations;

public class AlertEventConfiguration : IEntityTypeConfiguration<AlertEvent>
{
    public void Configure(EntityTypeBuilder<AlertEvent> builder)
    {
        builder.ToTable("AlertEvents");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.AlertId)
            .IsRequired();

        builder.Property(x => x.EventType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.PreviousStatus)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.NewStatus)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Payload)
            .HasColumnType("text");

        builder.Property(x => x.CorrelationId)
            .HasMaxLength(150);

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        // Indexes
        builder.HasIndex(x => x.AlertId);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.EventType);

        // Foreign Key
        builder.HasOne<Alert>()
            .WithMany()
            .HasForeignKey(x => x.AlertId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
