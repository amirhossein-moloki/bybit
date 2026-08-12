using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.SignalIntelligence.Entities;

namespace TradingBot.Persistence.SignalIntelligence.Configurations;

public class FailedMessageAnalysisConfiguration : IEntityTypeConfiguration<FailedMessageAnalysis>
{
    public void Configure(EntityTypeBuilder<FailedMessageAnalysis> builder)
    {
        builder.ToTable("FailedMessageAnalyses");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.MessageId)
            .IsRequired();

        builder.Property(x => x.FailureReason)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.Component)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.RetryCount)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.Property(x => x.ResolvedAt)
            .HasColumnType("timestamp with time zone");

        // Shadows
        builder.Property<DateTime?>("UpdatedAt")
            .HasColumnType("timestamp with time zone");

        // Relationships
        builder.HasOne<TelegramMessage>()
            .WithMany()
            .HasForeignKey(x => x.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        builder.HasIndex(x => x.MessageId);
        builder.HasIndex(x => x.Status);
    }
}
