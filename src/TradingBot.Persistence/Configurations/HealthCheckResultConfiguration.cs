using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence.Configurations;

public class HealthCheckResultConfiguration : IEntityTypeConfiguration<HealthCheckResult>
{
    public void Configure(EntityTypeBuilder<HealthCheckResult> builder)
    {
        builder.ToTable("HealthCheckResults");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ServiceName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.CheckedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.DurationMs)
            .IsRequired();

        builder.Property(x => x.ErrorCode)
            .HasMaxLength(100);

        builder.Property(x => x.ErrorMessage)
            .HasColumnType("text");

        builder.Property(x => x.Metadata)
            .HasColumnType("text");

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        // Shadow property for UpdatedAt to ensure DbContext UpdateTimestamps handles it and optimistic concurrency is ready
        builder.Property<DateTime?>("UpdatedAt")
            .HasColumnType("timestamp with time zone")
            .IsConcurrencyToken();

        // Indexes appropriate for performance queries (e.g. current health status or history checks)
        builder.HasIndex(x => x.ServiceName);
        builder.HasIndex(x => x.CheckedAt);
        builder.HasIndex(x => x.Status);
    }
}
