using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence.Configurations;

public class RiskRuleConfiguration : IEntityTypeConfiguration<RiskRule>
{
    public void Configure(EntityTypeBuilder<RiskRule> builder)
    {
        builder.ToTable("RiskRules");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.MaxRiskPercent)
            .HasColumnType("numeric(5,2)")
            .IsRequired();

        builder.Property(x => x.MaxOpenPositions)
            .IsRequired();

        builder.Property(x => x.MaxDailyLoss)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.MaxLeverage)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        // Shadow property for UpdatedAt
        builder.Property<DateTime?>("UpdatedAt")
            .HasColumnType("timestamp with time zone")
            .IsConcurrencyToken();
    }
}
