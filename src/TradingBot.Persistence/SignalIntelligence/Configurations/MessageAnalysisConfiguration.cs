using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.SignalIntelligence.Entities;

namespace TradingBot.Persistence.SignalIntelligence.Configurations;

public class MessageAnalysisConfiguration : IEntityTypeConfiguration<MessageAnalysis>
{
    public void Configure(EntityTypeBuilder<MessageAnalysis> builder)
    {
        builder.ToTable("MessageAnalyses", t =>
        {
            t.HasCheckConstraint("CK_MessageAnalyses_Confidence", "\"Confidence\" >= 0 AND \"Confidence\" <= 1");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TelegramMessageId)
            .IsRequired();

        builder.Property(x => x.MessageType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Confidence)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.ExtractedData)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.AIUsed)
            .IsRequired();

        builder.Property(x => x.ProcessedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        // Shadow property for UpdatedAt to match standard pattern
        builder.Property<DateTime?>("UpdatedAt")
            .HasColumnType("timestamp with time zone");

        // Relationships
        builder.HasOne<TelegramMessage>()
            .WithMany()
            .HasForeignKey(x => x.TelegramMessageId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(x => x.TelegramMessageId);
        builder.HasIndex(x => x.MessageType);
    }
}
