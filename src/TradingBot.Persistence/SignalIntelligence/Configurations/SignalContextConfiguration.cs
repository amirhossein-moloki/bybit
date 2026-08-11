using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.Entities;
using TradingBot.Domain.SignalIntelligence.Entities;

namespace TradingBot.Persistence.SignalIntelligence.Configurations;

public class SignalContextConfiguration : IEntityTypeConfiguration<SignalContext>
{
    public void Configure(EntityTypeBuilder<SignalContext> builder)
    {
        builder.ToTable("SignalContexts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.SignalId)
            .IsRequired();

        builder.Property(x => x.ChannelId)
            .IsRequired();

        builder.Property(x => x.Symbol)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.CurrentState)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.LastAction)
            .HasMaxLength(250);

        builder.Property(x => x.LastMessageId)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnType("timestamp with time zone");

        // Relationships
        builder.HasOne<Signal>()
            .WithMany()
            .HasForeignKey(x => x.SignalId)
            .OnDelete(DeleteBehavior.Restrict);

        // Indexes
        builder.HasIndex(x => x.SignalId);
        builder.HasIndex(x => new { x.ChannelId, x.Symbol });
    }
}
