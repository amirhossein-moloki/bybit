using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.SignalIntelligence.Entities;

namespace TradingBot.Persistence.SignalIntelligence.Configurations;

public class SignalExtractionConfiguration : IEntityTypeConfiguration<SignalExtraction>
{
    public void Configure(EntityTypeBuilder<SignalExtraction> builder)
    {
        builder.ToTable("SignalExtractions", t =>
        {
            t.HasCheckConstraint("CK_SignalExtractions_Confidence", "\"Confidence\" >= 0 AND \"Confidence\" <= 1");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TelegramMessageId)
            .IsRequired();

        builder.Property(x => x.MessageId)
            .IsRequired();

        builder.Property(x => x.Symbol)
            .HasMaxLength(20);

        builder.Property(x => x.Side)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.EntryPrice)
            .HasColumnType("numeric(18,8)");

        builder.Property(x => x.StopLoss)
            .HasColumnType("numeric(18,8)");

        builder.Property(x => x.TakeProfitData)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.Confidence)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasMaxLength(20)
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
        builder.HasIndex(x => x.MessageId);
        builder.HasIndex(x => x.Symbol);
        builder.HasIndex(x => x.Status);
    }
}
