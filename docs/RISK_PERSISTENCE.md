# Risk Persistence

This document explains the persistence layer design, EF Core configuration, and transaction model for risk management and trade decisions.

## Relational Schema Design

Two main tables exist in the database for tracking risk assessments and decisions:

### `RiskEvaluations`
Saves detailed risk calculation results and execution metrics.

- `Id` (uuid, Primary Key)
- `SignalId` (uuid, Foreign Key)
- `RiskAmount` (numeric(18,8))
- `PositionSize` (numeric(18,8))
- `RiskReward` (numeric(18,8))
- `Exposure` (numeric(18,8))
- `Decision` (varchar(50)) - Maps the `RiskDecisionStatus` enum.
- `Reason` (varchar(1000))
- `RiskLevel` (varchar(50)) - Maps the `RiskLevel` enum.
- `ExecutedRules` (text) - Serialized JSON collection of executed rule names.
- `PassedRules` (text) - Serialized JSON collection of passed rule names.
- `FailedRules` (text) - Serialized JSON collection of failed rule names.
- `ExecutionTime` (interval) - TimeSpan tracking duration of computation.
- `CreatedAt` (timestamp with time zone)

### `TradeDecisions`
Saves the final, immutable decision result of a signal valuation.

- `Id` (uuid, Primary Key)
- `SignalId` (uuid, Foreign Key)
- `Decision` (varchar(50)) - Maps `RiskDecisionStatus` enum.
- `DecisionReason` (varchar(1000))
- `RiskEvaluationId` (uuid, Foreign Key)
- `Status` (varchar(100)) - Text representation of approval status (Approved, Rejected, NeedsManualReview).
- `CreatedAt` (timestamp with time zone)

## EF Core Conversions

To ensure relational database compatibility across SQLite and PostgreSQL, complex and collection types are serialized directly via EF Core Value Converters inside `RiskEvaluationConfiguration`:

```csharp
builder.Property(x => x.ExecutedRules)
    .HasConversion(
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
    )
    .HasColumnType("text");
```

## Atomic Transaction Model

The Trade Decision workflow operates under a strict, single database transaction managed by the `UnitOfWork` pattern:

```text
Begin Transaction Async
          ↓
Update Signal (Mark Evaluation Started)
          ↓
Add Risk Evaluation Record
          ↓
Add Trade Decision Record
          ↓
Update Signal (Mark Trade Approved/Rejected/NeedsReview)
          ↓
Save Changes Async
          ↓
Commit Transaction Async
```

If any operation within this block throws an exception, the entire transaction is completely rolled back (`RollbackTransactionAsync`), ensuring no partial writes or state corruption occur.
