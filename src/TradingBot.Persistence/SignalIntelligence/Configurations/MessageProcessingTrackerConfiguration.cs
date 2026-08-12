using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.SignalIntelligence.Entities;

namespace TradingBot.Persistence.SignalIntelligence.Configurations;

public class MessageProcessingTrackerConfiguration : IEntityTypeConfiguration<MessageProcessingTracker>
{
    public void Configure(EntityTypeBuilder<MessageProcessingTracker> builder)
    {
        builder.ToTable("MessageProcessingTrackers");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TelegramMessageId)
            .IsRequired();

        builder.Property(x => x.State)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        // Relationships
        builder.HasOne<TelegramMessage>()
            .WithMany()
            .HasForeignKey(x => x.TelegramMessageId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        builder.HasIndex(x => x.TelegramMessageId);
        builder.HasIndex(x => x.State);
    }
}
