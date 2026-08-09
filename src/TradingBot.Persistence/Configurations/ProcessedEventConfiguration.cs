using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence.Configurations;

public class ProcessedEventConfiguration : IEntityTypeConfiguration<ProcessedEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedEvent> builder)
    {
        builder.ToTable("ProcessedEvents");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.EventId)
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(x => x.EventType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.PositionId);

        builder.Property(x => x.ExchangeOrderId)
            .HasMaxLength(100);

        builder.Property(x => x.ProcessedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        // Unique index on EventId
        builder.HasIndex(x => x.EventId)
            .IsUnique();
    }
}
