using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.RiskManagement.Entities;

namespace TradingBot.Persistence.Configurations;

public class RiskProfileConfiguration : IEntityTypeConfiguration<RiskProfile>
{
    public void Configure(EntityTypeBuilder<RiskProfile> builder)
    {
        builder.ToTable("RiskProfiles");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.MaxRiskPerTrade)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.MaxDailyLoss)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.MaxWeeklyLoss)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.MaxMonthlyLoss)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.MaxOpenPositions)
            .IsRequired();

        builder.Property(x => x.MaxLeverage)
            .IsRequired();

        builder.Property(x => x.MaxExposure)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.MinimumRiskReward)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();

        builder.Property(x => x.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsConcurrencyToken()
            .IsRequired();
    }
}
