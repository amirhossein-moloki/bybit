using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence.Configurations;

public class SymbolConfiguration : IEntityTypeConfiguration<Symbol>
{
    public void Configure(EntityTypeBuilder<Symbol> builder)
    {
        builder.ToTable("Symbols");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Exchange)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.SymbolCode)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.BaseAsset)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(x => x.QuoteAsset)
            .HasMaxLength(10)
            .IsRequired();

        builder.Property(x => x.TickSize)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.QuantityStep)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.MinQuantity)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        // Add shadow property for UpdatedAt to satisfy the requirement: "All tables: Required: CreatedAt, UpdatedAt"
        builder.Property<DateTime?>("UpdatedAt")
            .HasColumnType("timestamp with time zone")
            .IsConcurrencyToken();
    }
}
