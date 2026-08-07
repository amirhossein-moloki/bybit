using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TradingBot.Domain.RiskManagement.Entities;

namespace TradingBot.Persistence.Configurations;

public class RiskEvaluationConfiguration : IEntityTypeConfiguration<RiskEvaluation>
{
    public void Configure(EntityTypeBuilder<RiskEvaluation> builder)
    {
        builder.ToTable("RiskEvaluations");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.SignalId)
            .IsRequired();

        builder.Property(x => x.RiskAmount)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.PositionSize)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.RiskReward)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.Exposure)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(x => x.Decision)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Reason)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(x => x.RiskLevel)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.ExecutedRules)
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new System.Collections.Generic.List<string>()
            )
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.PassedRules)
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new System.Collections.Generic.List<string>()
            )
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.FailedRules)
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, (System.Text.Json.JsonSerializerOptions?)null),
                v => System.Text.Json.JsonSerializer.Deserialize<System.Collections.Generic.List<string>>(v, (System.Text.Json.JsonSerializerOptions?)null) ?? new System.Collections.Generic.List<string>()
            )
            .HasColumnType("text")
            .IsRequired();

        builder.Property(x => x.ExecutionTime)
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP")
            .IsRequired();
    }
}
