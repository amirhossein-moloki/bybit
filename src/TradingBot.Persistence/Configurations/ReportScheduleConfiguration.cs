using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence.Configurations;

public class ReportScheduleConfiguration : IEntityTypeConfiguration<ReportSchedule>
{
    public void Configure(EntityTypeBuilder<ReportSchedule> builder)
    {
        builder.ToTable("ReportSchedules");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ScheduleName)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.CronExpression)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.ReportType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.EmailRecipient)
            .HasMaxLength(250)
            .IsRequired();

        builder.Property(x => x.ExportFormat)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .IsRequired();

        // Shadow properties for CreatedAt/UpdatedAt
        builder.Property<DateTime>("CreatedAt")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.Property<DateTime?>("UpdatedAt")
            .HasColumnType("timestamp with time zone")
            .IsConcurrencyToken();

        // Indexes
        builder.HasIndex(x => x.IsActive);
    }
}
