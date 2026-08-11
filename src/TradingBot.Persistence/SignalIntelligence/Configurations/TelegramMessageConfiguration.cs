using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.SignalIntelligence.Entities;

namespace TradingBot.Persistence.SignalIntelligence.Configurations;

public class TelegramMessageConfiguration : IEntityTypeConfiguration<TelegramMessage>
{
    public void Configure(EntityTypeBuilder<TelegramMessage> builder)
    {
        builder.ToTable("TelegramMessages");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ChannelId)
            .IsRequired();

        builder.Property(x => x.MessageId)
            .IsRequired();

        builder.Property(x => x.SenderId);

        builder.Property(x => x.Content)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.ReceivedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.Processed)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        // Shadow property for UpdatedAt to match standard pattern
        builder.Property<DateTime?>("UpdatedAt")
            .HasColumnType("timestamp with time zone");

        // Unique constraint on ChannelId + MessageId
        builder.HasIndex(x => new { x.ChannelId, x.MessageId })
            .IsUnique();

        // Separate indexes for quick lookups
        builder.HasIndex(x => x.ChannelId);
        builder.HasIndex(x => x.MessageId);
        builder.HasIndex(x => x.Processed);
    }
}
