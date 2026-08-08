# Phase 06 — Stage 05: Execution Finalization, End-to-End Workflow & Production Readiness

This document describes the final execution architecture, end-to-end trading workflow, event-driven pipeline, real-time observability, and failure recovery mechanics implemented in Stage 05.

---

## 1. Complete End-to-End Trading Workflow

The Telegram Signal Trading Bot integrates all subsystems into a unified, high-reliability execution pipeline:

```text
Telegram Signal ──► Signal Parser ──► Validation Engine ──► Risk Engine ──► Trade Decision
                                                                                   │
                                                                                   ▼
Bybit Testnet  ◄── Exchange Gateway ◄── Order Service ◄── Execution Coordinator (Orchestrator)
      │
      ▼
Order Tracking ──► Reconciliation ──► Database ──► Audit System + Observability
```

1. **Telegram Ingestion**: Incoming signal alerts are detected, parsed, and mapped to domain-level `Signal` entities.
2. **Signal Validation**: The pipeline validates syntax, symbol mapping, direction, and consistency checks.
3. **Risk Management Evaluation**: The `TradeDecisionWorkflow` evaluates the signal against 9 protection rules. It persists a `RiskEvaluation` and `TradeDecision` inside an atomic transaction.
4. **Execution Orchestration**: Approved decisions trigger the `TradeExecutionOrchestrator` to coordinate the final order submission, tracking, event publishing, and persistent auditable logging.

---

## 2. Final Execution Architecture

The execution pipeline decouples concerns using a high-level `TradeExecutionOrchestrator` that coordinates lower-level services without containing direct exchange, database, or validation business logic:

- **ITradeExecutionOrchestrator**: Final execution coordinator. Verifies risk decision state, validates execution request via `IOrderValidator`, checks idempotency / duplicate request via `IOrderRepository`, executes order via `ITradeExecutionService`, publishes domain events, and saves changes.
- **IExecutionEventPublisher / IExecutionEventPipeline**: An in-memory, thread-safe publishing pipeline that invokes registered handlers to decouple side-effects from execution flow.
- **IExecutionMetrics / ExecutionMetrics**: Thread-safe live statistics container tracking pipeline latencies, counts, rates, and durations.

---

## 3. Execution Domain Events & Pipeline

All status transitions publish immutable, serializable, and auditable domain events to the event pipeline:

```text
Execution Event ──► Event Handler ──► Structured Logging (Serilog)
                                  ──► Persistent Audits (SystemLogs DB)
                                  ──► Live Metrics (ExecutionMetrics)
```

### Supported Events

- `TradeExecutionStartedEvent`: Published when the coordinator receives the execution request.
- `OrderSubmissionStartedEvent`: Published when the local order is created as `Pending`/`Submitting` before gateway call.
- `OrderSubmittedEvent`: Published when the order has been successfully sent to and accepted by Bybit (`New` or `Accepted`).
- `OrderFilledEvent`: Published when the order is completely filled on the exchange.
- `OrderRejectedEvent`: Published when the order is rejected by validator, risk boundaries, or exchange constraints.
- `OrderFailedEvent`: Published when submission fails permanently.
- `TradeExecutionCompletedEvent`: Terminal event signaling completion with duration and success indicator.

---

## 4. Failure Recovery & Reliability Scenarios

The execution orchestrator is engineered to handle common production failures gracefully.

### Scenario 1: Risk Approved, Exchange Unavailable

- **Behavior**: The local order is pre-generated in `Pending` then marked as `Submitting`. If the Bybit API is unreachable or returns a network error, the exception is intercepted.
- **Action**: The local order is transitioned to `Unknown` state if the error might be transient (e.g. timeout), or `Failed` if permanent. The failure reason and code are persisted, and the status is reconciled asynchronously by `OrderReconciliationWorker`. No blind retries occur on create-order endpoint.

### Scenario 2: Application Crash Post-Submission

- **Behavior**: The order was successfully submitted, but the application crashed before persisting the exchange's response.
- **Action**: Upon restart, the `OrderReconciliationWorker` scans for local orders in active or unresolved states (including `Pending`, `Submitting`, and `Unknown`). It queries Bybit using `GetOrderAsync` mapped with the deterministic `ClientOrderId` (`TB-{OrderId}`). The local database progressively recovers the final state from the exchange.

### Scenario 3: Concurrent Duplicate Execution Request

- **Behavior**: Multiple duplicate signals trigger execution simultaneously.
- **Action**: The coordinator checks `IOrderRepository.GetBySignalIdAsync` BEFORE submission. If an existing order is found for the given `SignalId`, the orchestrator returns the existing execution details immediately without contacting the exchange. In case of database race conditions, unique key index on `SignalId` prevents duplicate insertion at the DB engine level.

### Scenario 4: Exchange Timeout (Unknown State)

- **Behavior**: The exchange receives the order, but the HTTP connection drops before returning the response.
- **Action**: The gateway catches the timeout, maps it to `OrderStatus.Unknown`, and writes `TIMEOUT` error codes. The background `OrderReconciliationService` queries Bybit. If found on exchange, the order is progressive-updated (e.g., `New` or `Filled`). If not found (meaning it never made it to the exchange), it transitions safely to `Failed`.

---

## 5. Observability (Metrics & Health Checks)

Real-time monitoring and production health checks are fully integrated:

### Execution Metrics

- `Total Executions`: Total count of initiated execution runs.
- `Successful Executions`: Count of successfully completed filled/submitted orders.
- `Failed Executions`: Count of validation/risk/network failed executions.
- `Rejected Orders` / `Filled Orders`: Absolute status counts.
- `Average Execution Time`: Precise execution duration calculation.

### Exchange Metrics

- `API Latency` / `API Errors`: Track gateway performance.
- `Rate Limit Hits` / `Timeout Count`: Count rate-limits and timeouts.

### Database Metrics

- `Order Persistence Time`: Track average database transaction write latency.
- `Reconciliation Duration`: Asynchronous reconciliation pass timing.
- `Failed Transactions`: Count transaction failures.

### Extended Health Monitoring

The `TradingEngineHealthCheck` checks:
1. **DI Registration**: Verifies that orchestrator, metrics, and event handlers are fully registered and resolvable.
2. **Reconciliation Worker Heartbeat**: Tracks the static `OrderReconciliationService.LastRunTime`. If the reconciliation pass has not run within the last 30 seconds, it reports `Unhealthy` or `Degraded`.

---

## 6. Production Security & Redaction Checklist

- [x] **Exchange API Key Security**: Permissions are verified to be "Trade" only. External withdraw functions are disabled.
- [x] **Redacted Logging**: Serilog configuration incorporates Regex masking inside `SystemLog` to dynamically redact both key labels (e.g. `secret_key`, `api_key`, `password`) and actual credentials in execution log parameters.
- [x] **Encrypted Session Persistence**: Secure Telegram session storage uses an `EncryptedSessionStream` using AES-256 encryption.
- [x] **Secure Configuration**: External keys are configured using environment variable overrides (`BYBIT_API_KEY`, `BYBIT_SECRET_KEY`, `DATABASE_CONNECTION`) with zero hardcoded repository credentials.
- [x] **Fail-Closed Operations**: If `InstrumentRules` are missing from the configuration or database, order validation fails closed immediately.
