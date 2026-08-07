# Risk Decision Workflow

This document details the architecture and flow of the Trade Decision Workflow in the Trading Bot's Risk Management Engine.

## Workflow Overview

The `TradeDecisionWorkflow` coordinates the entire risk management process for a validated signal, transforming raw signals into trade decisions with traceably persisted metrics.

```text
Validated Signal
        ↓
TradeRiskContext
        ↓
Risk Calculation
        ↓
Risk Rule Engine
        ↓
Trade Decision
        ↓
Persistence (RiskEvaluation + TradeDecision + Signal Status in Single Transaction)
        ↓
Audit Trail (Immutable log records)
        ↓
Ready For Trading Execution Engine
```

## Sequence of Steps

1. **Duplicate Protection**:
   Checks the database for an existing evaluation matching the `SignalId`. If found, it returns the existing results and outputs a warning log, skipping redundant calculations.
2. **Transaction Initialization**:
   Starts an EF Core transaction to guarantee atomic database updates.
3. **Audit Initiation**:
   Triggers a log entry recording that the evaluation has started.
4. **Lifecycle State Transition**:
   Updates the `Signal.Status` to `RiskEvaluationStarted` to reflect that processing is underway.
5. **Evaluation Execution**:
   Executes modular calculators (Risk Amount, Position Size, Risk Reward, Stop Loss Distance) and runs all registered rules through the `RiskRuleEngine`.
6. **Decision Priority Logic**:
   Computes the final trade decision (Approved, Rejected, or NeedsManualReview) based on rule severities:
   - **Critical Rule Failed** -> `Rejected`
   - **Only Warnings** -> `Approved`
   - **Unexpected Error** -> `NeedsManualReview`
7. **Signal State Updates**:
   Depending on the calculated decision:
   - Approved -> Transitions signal to `TradeApproved`
   - Rejected -> Transitions signal to `TradeRejected`
   - NeedsReview/ManualReview -> Transitions signal to `ManualReview`
8. **Persistence**:
   Inserts the `RiskEvaluation` and `TradeDecision` records into the database and updates the `Signal`.
9. **Atomic Save and Commit**:
   Performs a single transaction-saving commit. If any step fails, the transaction is completely rolled back to avoid database corruption.
10. **Immutable Audit Record**:
    Logs rules executed, rule failures, processing duration, and the final decision.

## Error Recovery Strategy

- **Database / Save Failures**: Automatically rolls back the transaction, logs a critical error, and fails the workflow.
- **Rule execution Exception**: Reverts to a fallback `NeedsManualReview` decision, preserving workflow execution without database corruption.
