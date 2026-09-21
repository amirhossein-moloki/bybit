using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.SignalIntelligence.Entities;

namespace TradingBot.Persistence.Configurations;

public class MessageProcessingAttemptConfiguration : IEntityTypeConfiguration<MessageProcessingAttempt>
{
    public void Configure(EntityTypeBuilder<MessageProcessingAttempt> builder)
    {
        builder.ToTable("MessageProcessingAttempts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TelegramMessageId)
            .IsRequired();

        builder.Property(x => x.AttemptNumber)
            .IsRequired();

        builder.Property(x => x.TriggerType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.TriggeredBy)
            .HasMaxLength(100);

        builder.Property(x => x.ProcessingMode)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.ParserVersion)
            .HasMaxLength(50);

        builder.Property(x => x.AiModelVersion)
            .HasMaxLength(100);

        builder.Property(x => x.Status)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Intent)
            .HasMaxLength(50);

        builder.Property(x => x.Symbol)
            .HasMaxLength(50);

        builder.Property(x => x.Side)
            .HasMaxLength(20);

        builder.Property(x => x.EntryPrice)
            .HasPrecision(18, 8);

        builder.Property(x => x.StopLoss)
            .HasPrecision(18, 8);

        builder.Property(x => x.TakeProfitsJson)
            .HasColumnType("text");

        builder.Property(x => x.Leverage)
            .HasPrecision(18, 4);

        builder.Property(x => x.SpreadAllowance)
            .HasMaxLength(100);

        builder.Property(x => x.ValidationResult)
            .HasMaxLength(50);

        builder.Property(x => x.RiskResult)
            .HasMaxLength(50);

        builder.Property(x => x.ExecutionResult)
            .HasMaxLength(100);

        builder.Property(x => x.ErrorMessage)
            .HasColumnType("text");

        builder.Property(x => x.MetadataJson)
            .HasColumnType("text");

        builder.Property(x => x.StartedAt)
            .IsRequired();

        builder.Property(x => x.CompletedAt);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        // Foreign key relation to TelegramMessage
        builder.HasOne<TelegramMessage>()
            .WithMany()
            .HasForeignKey(x => x.TelegramMessageId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        builder.HasIndex(x => x.TelegramMessageId);
        builder.HasIndex(x => new { x.TelegramMessageId, x.AttemptNumber }).IsUnique();
        builder.HasIndex(x => x.Status);
    }
}
