using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence.Configurations;

public class SignalConfiguration : IEntityTypeConfiguration<Signal>
{
    public void Configure(EntityTypeBuilder<Signal> builder)
    {
        builder.ToTable("Signals", t =>
        {
            t.HasCheckConstraint("CK_Signals_Quantity", "\"Quantity\" > 0");
            t.HasCheckConstraint("CK_Signals_EntryPrice", "\"EntryPrice\" >= 0");
            t.HasCheckConstraint("CK_Signals_Price", "\"Price\" >= 0");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Source)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.RawMessage)
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.Symbol)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Side)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.EntryPrice)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.Quantity)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.StopLoss)
            .HasColumnType("numeric(18,8)");

        builder.Property(x => x.TakeProfit)
            .HasColumnType("numeric(18,8)");

        builder.Property(x => x.Leverage);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // Backward compatibility properties
        builder.Property(x => x.Type)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Price)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        // Shadow property for UpdatedAt
        builder.Property<DateTime?>("UpdatedAt")
            .HasColumnType("timestamp with time zone")
            .IsConcurrencyToken();

        // Indexes
        builder.HasIndex(x => x.Symbol);
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.CreatedAt);
    }
}
