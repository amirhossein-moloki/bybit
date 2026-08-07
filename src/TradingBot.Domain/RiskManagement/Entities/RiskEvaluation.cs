using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using TradingBot.Domain.RiskManagement.Enums;

namespace TradingBot.Domain.RiskManagement.Entities;

public class RiskEvaluation
{
    public Guid Id { get; set; }
    public Guid SignalId { get; set; }
    public decimal RiskAmount { get; set; }
    public decimal PositionSize { get; set; }
    public decimal RiskReward { get; set; }
    public decimal Exposure { get; set; }
    public RiskDecisionStatus Decision { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Extended Audit/Engine properties for Phase 05 Stage 03 - marked as [NotMapped] to preserve database schema integrity
    [NotMapped]
    public IReadOnlyList<string> ExecutedRules { get; set; } = Array.Empty<string>();

    [NotMapped]
    public IReadOnlyList<string> PassedRules { get; set; } = Array.Empty<string>();

    [NotMapped]
    public IReadOnlyList<string> FailedRules { get; set; } = Array.Empty<string>();

    [NotMapped]
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();

    [NotMapped]
    public IReadOnlyList<string> Errors { get; set; } = Array.Empty<string>();

    [NotMapped]
    public TimeSpan ExecutionTime { get; set; }

    [NotMapped]
    public RiskLevel RiskLevel { get; set; }

    public RiskEvaluation()
    {
        Id = Guid.NewGuid();
    }
}
