using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.Entities;

namespace TradingBot.Persistence.Configurations;

public class StopLossHistoryConfiguration : IEntityTypeConfiguration<StopLossHistory>
{
    public void Configure(EntityTypeBuilder<StopLossHistory> builder)
    {
        builder.ToTable("StopLossHistories");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PositionId)
            .IsRequired();

        builder.Property(x => x.OldPrice)
            .HasColumnType("numeric(18,8)");

        builder.Property(x => x.NewPrice)
            .HasColumnType("numeric(18,8)");

        builder.Property(x => x.Reason)
            .HasMaxLength(250)
            .IsRequired();

        builder.Property(x => x.Source)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.HasIndex(x => x.PositionId);
    }
}
