using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence.Configurations;

public class MonitoringEventConfiguration : IEntityTypeConfiguration<MonitoringEvent>
{
    public void Configure(EntityTypeBuilder<MonitoringEvent> builder)
    {
        builder.ToTable("MonitoringEvents");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.EventType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Severity)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Source)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Component)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Message)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.CorrelationId)
            .HasMaxLength(150);

        builder.Property(x => x.OperationId)
            .HasMaxLength(150);

        builder.Property(x => x.SignalId);
        builder.Property(x => x.OrderId);
        builder.Property(x => x.PositionId);

        builder.Property(x => x.Payload)
            .HasColumnType("text");

        builder.Property(x => x.ErrorCode)
            .HasMaxLength(100);

        builder.Property(x => x.ExceptionType)
            .HasMaxLength(200);

        builder.Property(x => x.ExternalEventId)
            .HasMaxLength(150);

        builder.Property(x => x.Timestamp)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        // Shadow property for UpdatedAt
        builder.Property<DateTime?>("UpdatedAt")
            .HasColumnType("timestamp with time zone");

        // Indexes for frequent queries
        builder.HasIndex(x => x.Timestamp);
        builder.HasIndex(x => x.EventType);
        builder.HasIndex(x => x.Severity);
        builder.HasIndex(x => x.Source);
        builder.HasIndex(x => x.CorrelationId);
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.PositionId);

        // Idempotency: Unique index on (Source, ExternalEventId) where ExternalEventId is not null
        builder.HasIndex(x => new { x.Source, x.ExternalEventId })
            .IsUnique()
            .HasFilter("\"ExternalEventId\" IS NOT NULL");
    }
}
