using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence.Configurations;

public class SystemLogConfiguration : IEntityTypeConfiguration<SystemLog>
{
    public void Configure(EntityTypeBuilder<SystemLog> builder)
    {
        builder.ToTable("SystemLogs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Level)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Category)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Message)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.Exception)
            .HasColumnType("text");

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        // Shadow property for UpdatedAt
        builder.Property<DateTime?>("UpdatedAt")
            .HasColumnType("timestamp with time zone");
    }
}
