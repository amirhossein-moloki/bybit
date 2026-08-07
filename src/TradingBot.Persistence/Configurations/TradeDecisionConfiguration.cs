using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.RiskManagement.Entities;

namespace TradingBot.Persistence.Configurations;

public class TradeDecisionConfiguration : IEntityTypeConfiguration<TradeDecision>
{
    public void Configure(EntityTypeBuilder<TradeDecision> builder)
    {
        builder.ToTable("TradeDecisions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.SignalId)
            .IsRequired();

        builder.Property(x => x.Decision)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.DecisionReason)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(x => x.RiskEvaluationId)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();
    }
}
