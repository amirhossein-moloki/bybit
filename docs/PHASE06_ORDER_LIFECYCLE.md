# Phase 06 — Stage 04: Order Lifecycle, Persistence & Execution Reliability

This document describes the design, implementation, and verification of the resilient order lifecycle, persistence, and exchange execution state machine.

---

## 1. Order State Machine

The order execution engine enforces a strict, explicit state machine inside the `Order` domain entity. State transitions are verified programmatically on every change to prevent state downgrades or regressions (e.g. transitioning a terminal status back to an active state).

### Transition Flow

```text
       [Created]
           │
           ▼
       [Pending] <─────────────────────────────┐
           │                                   │ (Validation failure recovery)
           ▼                                   │
      [Submitting] ───► [Unknown] (Timeout) ───┼─► [Reconciled]
           │                  │                │
           ▼                  ▼                │
      [Submitted] ───► [New/Accepted] ─────────┘
           │                  │
           ▼                  ▼
    [PartiallyFilled] ──► [Filled] (Terminal)
           │
           ▼
    [Cancelled/Expired] (Terminal)
```

### Valid Transition Mapping

- **Created** $\rightarrow$ `Pending`, `ValidationFailed`, `ReadyForExchange`, `Submitting`, `Submitted`, `Rejected`.
- **Pending** $\rightarrow$ `Submitting`, `ReadyForExchange`, `ValidationFailed`, `Failed`, `Rejected`.
- **Submitting** $\rightarrow$ `Submitted`, `Accepted`, `New`, `PartiallyFilled`, `Filled`, `Cancelled`, `Rejected`, `Failed`, `Unknown`.
- **Submitted** $\rightarrow$ `New`, `Accepted`, `PartiallyFilled`, `Filled`, `Cancelled`, `Rejected`, `Failed`, `Unknown`.
- **Accepted** $\rightarrow$ `PartiallyFilled`, `Filled`, `Cancelled`, `Rejected`, `Failed`.
- **ReadyForExchange** $\rightarrow$ `ValidationFailed`, `Pending`, `Submitting`, `Failed`, `Rejected`.
- **New** $\rightarrow$ `PartiallyFilled`, `Filled`, `Cancelled`, `Rejected`, `Expired`, `Unknown`.
- **PartiallyFilled** $\rightarrow$ `Filled`, `Cancelled`, `Expired`.
- **Unknown** $\rightarrow$ `New`, `PartiallyFilled`, `Filled`, `Cancelled`, `Rejected`, `Failed`, `Expired`.
- **Terminal States** (`Filled`, `Cancelled`, `Rejected`, `Failed`, `Expired`, `ValidationFailed`) cannot transition to any other status.

---

## 2. Persistence Model

The `Orders` database schema has been extended to support full exchange execution metadata with appropriate financial decimal precision (`numeric(18,8)`):

- **Exchange**: Identifier for target exchange (defaults to `"Bybit"`).
- **ExecutedQuantity**: Cumulative quantity executed on the exchange.
- **ExecutedPrice**: Weighted average execution price.
- **RequestedPrice**: Maps back to the limit price.
- **FailureReason**: Error message from the exchange/validation pipeline.
- **ExchangeErrorCode**: Code returned by the exchange API.
- **SubmittedAt**: Timestamp when the order was submitted to the exchange.
- **FilledAt**: Timestamp when the order was fully filled.
- **CancelledAt**: Timestamp when the order was cancelled.

---

## 3. Client Order ID Strategy

Each order is assigned a deterministic, unique client-side identifier used for duplicate prevention and exchange correlation:
- **Format**: `TB-{OrderId}` (where `{OrderId}` is the pre-generated GUID primary key of the local `Order` record).
- **Length**: 35 characters, perfectly matching Bybit V5 Unified API's maximum length restriction of 36 characters for `orderLinkId`.
- **Uniqueness**: Persisted in the database under a unique index constraint (`ClientOrderId UNIQUE`) BEFORE submission.

---

## 4. Idempotency & Concurrency

The trading system prevents duplicate submissions at two layers:
1. **Application Layer**: Prior to validation and creation, `GetBySignalIdAsync` is called on `IOrderRepository`. If an existing execution is found for the given `SignalId`, the existing `ExecutionResult` is returned immediately without contacting the exchange.
2. **Database Layer**: A unique index on `SignalId` prevents concurrent database insertion of duplicate intent, forcing database integrity under high-concurrency races.

---

## 5. Execution Transaction Boundaries

To prevent connection pool exhaustion and database locks during external network latency, database transaction boundaries are explicitly separated from exchange HTTP requests:

1. **Transaction 1 (Create Pending)**:
   - Create local `Order` in `Pending` state.
   - Insert order and audit event into DB.
   - Commit transaction and release connection.
2. **Transaction 2 (Mark Submitting)**:
   - Transition order status to `Submitting`.
   - Update in DB and commit.
3. **Gateway Submission (No Transaction)**:
   - Invoke Bybit Create Order API outside of any database transaction.
4. **Transaction 3 (Update Outcome)**:
   - Open separate transaction.
   - If successful, link `ExchangeOrderId` and update status to `Submitted` / `New` / `Filled`.
   - If failed/timeout, update status to `Rejected` / `Failed` or `Unknown`.
   - Commit transaction.

---

## 6. Unknown Execution Handling & Reconciliation

If a timeout, network disruption, or connection drop occurs during order creation:
- The system classifies the error as an **Unknown Result**.
- The local order is marked as `OrderStatus.Unknown`.
- The background `OrderReconciliationWorker` retrieves orders in active states (including `Unknown`) in safe batch sizes of 50.
- The `OrderReconciliationService` queries Bybit using the smart `GetOrderAsync` with `ClientOrderId` (`orderLinkId`).
- If found, it recovers the exchange details and updates the local order status progressively.
- If not found (never made it to the exchange), it transitions the local status safely to `Failed`.

---

## 7. Partial Fills & Weighted Price Calculations

When multiple execution fills occur, the average executed price is calculated as a mathematically precise weighted average:

$$\text{ExecutedPrice} = \frac{\sum (\text{Qty}_i \times \text{Price}_i)}{\sum \text{Qty}_i}$$

Accumulated executions are calculated using `RecordExecution(qty, price)`, which manages partial fills and advances the status to `PartiallyFilled` or `Filled` when the total quantity is met.

---

## 8. Append-Only Order Audit Trail

Every order status transition is logged in an append-only audit trail table `OrderEvents`. This table contains:
- **Id**: Guid primary key.
- **OrderId**: Foreign key referencing `Orders`.
- **PreviousStatus** & **NewStatus**: State change record.
- **EventType**: Type classification (e.g. `OrderCreated`, `ExchangeSubmissionSucceeded`, `OrderStateConflict`).
- **Source**: Context creator.
- **Message**: Rich text detail of the transition.
- **CreatedAt**: Timestamp.
