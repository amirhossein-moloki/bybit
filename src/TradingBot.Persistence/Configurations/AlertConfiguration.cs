using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence.Configurations;

public class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        builder.ToTable("Alerts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.RuleId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.AlertType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Severity)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Source)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Component)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Message)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.Payload)
            .HasColumnType("text");

        builder.Property(x => x.CorrelationId)
            .HasMaxLength(150);

        builder.Property(x => x.DeduplicationKey)
            .HasMaxLength(250)
            .IsRequired();

        builder.Property(x => x.TriggeredAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.LastSeenAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.ResolvedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.TriggerCount)
            .IsRequired();

        builder.Property(x => x.NotificationCount)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsConcurrencyToken();

        // Indexes
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.RuleId);
        builder.HasIndex(x => x.TriggeredAt);
        builder.HasIndex(x => x.LastSeenAt);
        builder.HasIndex(x => x.CorrelationId);

        // Unique filtered index to ensure concurrency safety on active alerts
        builder.HasIndex(x => x.DeduplicationKey)
            .IsUnique()
            .HasFilter("Status != 'Resolved'");
    }
}
