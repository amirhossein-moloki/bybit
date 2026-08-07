# Risk Audit System

This document outlines the audit logging strategy and implementation for the Risk Management Engine.

## Strategy

The `RiskAuditService` is tasked with maintaining an immutable and complete record of each stage of a trade evaluation. It writes structured log entries into the `SystemLogs` table using the domain-specific audit factory `SystemLog.CreateAuditLog(...)`.

Audit records are designed to be completely immutable and secure, containing no trace of sensitive API keys, secrets, or personal Telegram sessions.

## Recorded Operations

The audit log captures five specific operational stages of evaluation:

1. **Evaluation Started**:
   Records that a risk evaluation was initiated for a given `SignalId`.
   *Log format:* `[Audit] Op: RiskEvaluation | Entity: Signal (<SignalId>) | Desc: Evaluation Started`

2. **Rules Executed**:
   Lists all risk rules executed sequentially.
   *Log format:* `[Audit] Op: RiskEvaluationRules | Entity: Signal (<SignalId>) | Desc: Rules Executed: Rule1, Rule2`

3. **Rule Failures**:
   Lists rule names and failure messages if any are violated during evaluation.
   *Log format:* `[Audit] Op: RiskEvaluationFailures | Entity: Signal (<SignalId>) | Desc: Rule Failures: Message1`

4. **Final Decision**:
   Persists the final computed decision status and aggregation reasons.
   *Log format:* `[Audit] Op: RiskEvaluationDecision | Entity: Signal (<SignalId>) | Desc: Final Decision: Approved | Reason: Passed`

5. **Processing Duration**:
   Tracks execution times in milliseconds to guarantee compliance with target speeds (< 100 ms).
   *Log format:* `[Audit] Op: RiskEvaluationDuration | Entity: Signal (<SignalId>) | Desc: Processing Duration: X ms`

## Immutability & Security

- **SystemLog Entities**: Once written, logs can only be inserted, never updated or deleted.
- **Redaction Utility**: The static constructor sanitizes sensitive strings dynamically, automatically replacing fields matching key labels (like `api_key` or `secret`) with `[REDACTED]`.
