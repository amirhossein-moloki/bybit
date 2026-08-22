using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.Entities;
using TradingBot.Domain.Enums;

namespace TradingBot.Persistence.Configurations;

public class TelegramSourceConfiguration : IEntityTypeConfiguration<TelegramSource>
{
    public void Configure(EntityTypeBuilder<TelegramSource> builder)
    {
        builder.ToTable("TelegramSources");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TelegramChatId)
            .IsRequired();

        builder.Property(x => x.Title)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.Username)
            .HasMaxLength(128);

        builder.Property(x => x.Type)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.IsEnabled)
            .IsRequired();

        builder.Property(x => x.ListenForSignals)
            .IsRequired();

        builder.Property(x => x.ProcessMessages)
            .IsRequired();

        builder.Property(x => x.PausedUntil)
            .HasColumnType("timestamp with time zone");

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        // Unique constraint on TelegramChatId
        builder.HasIndex(x => x.TelegramChatId)
            .IsUnique();

        // Index for querying active sources efficiently
        builder.HasIndex(x => new { x.IsEnabled, x.ListenForSignals });
    }
}
