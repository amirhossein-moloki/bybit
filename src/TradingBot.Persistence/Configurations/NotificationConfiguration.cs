using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.EventId)
            .IsRequired();

        builder.Property(x => x.EventType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Severity)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Channel)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Recipient)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Message)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.Payload)
            .HasColumnType("text");

        builder.Property(x => x.CorrelationId)
            .HasMaxLength(100);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.AttemptCount)
            .IsRequired();

        builder.Property(x => x.MaxAttempts)
            .IsRequired();

        builder.Property(x => x.LastAttemptAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.NextAttemptAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.DeliveredAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.FailedAt)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.FailureReason)
            .HasColumnType("text");

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .IsConcurrencyToken();

        // Relationship
        builder.HasMany(x => x.DeliveryAttempts)
            .WithOne()
            .HasForeignKey(x => x.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.NextAttemptAt);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => x.EventId);
        builder.HasIndex(x => x.CorrelationId);
        builder.HasIndex(x => x.Channel);
    }
}
