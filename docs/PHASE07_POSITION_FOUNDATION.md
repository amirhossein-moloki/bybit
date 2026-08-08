# Position Foundation & Lifecycle (Phase 07 - Stage 01)

## Overview

The Position Management System introduces the domain logic and persistent infrastructure required to track and update trading positions after an executed order has been successfully filled.

This document covers the core architecture, financial precision rules, state machine transitions, database relationships, idempotency strategy, and test coverage for the Position Foundation.

---

## 1. Domain Models

### Position

The core `Position` domain model is located at `src/TradingBot.Domain/Entities/Position.cs`. It captures the state of an open, partially closed, or closed position:

*   **Id**: Unique identifier (`Guid`).
*   **OrderId**: The ID of the successful Order that opened this position.
*   **ExchangePositionId**: The raw ID of the position returned by the exchange.
*   **Symbol**: The normalized trading pair (e.g., `BTCUSDT`).
*   **Side**: `OrderSide.Buy` (Long) or `OrderSide.Sell` (Short).
*   **EntryPrice**: The execution entry price of the fill.
*   **CurrentPrice**: The latest tracked market price.
*   **Quantity**: The initial execution size of the position.
*   **RemainingQuantity**: The remaining position size after partial closures.
*   **StopLoss / TakeProfit**: Custom rule boundaries extracted from the original parsed signal.
*   **Leverage / Margin**: Leverage multiplier and initial collateral margin.
*   **UnrealizedPnL**: Floating profit/loss calculated as:
    *   *Long*: `(CurrentPrice - EntryPrice) * RemainingQuantity`
    *   *Short*: `(EntryPrice - CurrentPrice) * RemainingQuantity`
*   **RealizedPnL**: Accumulated profit/loss locked in from full or partial closures.
*   **Fee**: Accumulated exchange transaction fees.
*   **Status**: `PositionStatus` enum (`Pending`, `Open`, `PartiallyClosed`, `Closed`, `Liquidated`).
*   **OpenedAt / ClosedAt / UpdatedAt**: Standard lifecycle timestamps.

### PositionTarget

The `PositionTarget` domain model (`src/TradingBot.Domain/Entities/PositionTarget.cs`) tracks target price levels for partial closures:

*   **Id**: Unique identifier (`Guid`).
*   **PositionId**: The parent position.
*   **TargetNumber**: Sequential target index (e.g., 1, 2, 3).
*   **Price / Quantity / Percentage**: Target details.
*   **Status**: Target status (`Pending`, `Executed`).

### PositionEvent

The `PositionEvent` domain model (`src/TradingBot.Domain/Entities/PositionEvent.cs`) acts as an immutable audit log for position events:

*   **Id**: Unique identifier (`Guid`).
*   **PositionId**: The parent position.
*   **EventType**: Event type (`PositionOpened`, `PositionClosed`, `PositionPartiallyClosed`, `PositionLiquidated`).
*   **Payload**: Serialized payload containing execution and transition data.

---

## 2. State Machine & Transition Rules

Positions follow a strict, non-exceptional state machine:

```text
       Pending
          │
          ▼
        Open <───────────────┐
          │                  │
          ├──► PartiallyClosed─┘
          │          │
          ▼          ▼
        Closed   Liquidated
```

### Transition Validation Rules

1.  **Pending** can transition to **Open** or **Closed** (if aborted/cancelled).
2.  **Open** can transition to **PartiallyClosed**, **Closed**, or **Liquidated**.
3.  **PartiallyClosed** can transition to **PartiallyClosed** (further closures), **Closed** (if completely closed), or **Liquidated**.
4.  **Closed** and **Liquidated** are terminal states and **cannot** transition to any other status.

---

## 3. Position Application Service & Idempotency

`IPositionService` and `PositionService` (`src/TradingBot.Application/Services/PositionService.cs`) coordinate application operations:

*   **Idempotency & Duplicate Protection**: Before creating a position, the service queries the database for an existing position with the same `OrderId`. If a duplicate is detected, it returns the existing position atomically, preventing double creation.
*   **Execution Mapping**: The service uses executed quantity and price from successful fills rather than requested order values to maintain financial accuracy.
*   **Atomic Persistence**: Position creation, corresponding targets initialization, and initial `PositionOpened` audit events are persisted atomically within a single database transaction via the **Unit of Work**.

---

## 4. Database Mapping & Performance

Configurations are defined under `src/TradingBot.Persistence/Configurations/`:

1.  **Precision**: Financial decimal values are explicitly mapped to PostgreSQL `numeric(18,8)` types.
2.  **Constraints**: Check constraints enforce domain invariants in SQL:
    *   `CK_Positions_Quantity`: `"Quantity" > 0`
    *   `CK_Positions_RemainingQuantity`: `"RemainingQuantity" >= 0`
    *   `CK_Positions_EntryPrice`: `"EntryPrice" >= 0`
    *   `CK_Positions_CurrentPrice`: `"CurrentPrice" >= 0`
3.  **Indexes**: Highly optimized queries are supported using indexes:
    *   `Positions.OrderId` (unique)
    *   `Positions.ExchangePositionId`
    *   `Positions.Symbol`
    *   `Positions.Status`
    *   `PositionTargets.PositionId`
    *   `PositionEvents.PositionId`

---

## 5. Test Coverage

The Position Foundation has 100% test pass rate with two separate test suites:

### Unit Tests (`tests/TradingBot.UnitTests/Entities/PositionTests.cs`)

*   **Side Mapping**: Validates side rules and PnL calculation formulas for both long and short positions.
*   **Validation Rules**: Verifies boundaries (quantity, entry prices, invalid symbols).
*   **State Machine Transitions**: Confirms all valid transitions are allowed and all invalid transitions (e.g. Closed -> Open) are rejected with a `DomainException`.
*   **Partial Closures**: Validates remaining quantity decreases, fee increments, and correct accumulation of realized PnL.

### Integration Tests (`tests/TradingBot.IntegrationTests/Services/PositionServiceIntegrationTests.cs`)

*   **Successful Flow**: Simulates a completely filled order, executes the service, and verifies the position, targets, and events are saved atomically with correct properties.
*   **Idempotency / Duplication**: Verifies subsequent position creation calls on the same order return the existing position and do not duplicate database rows.
*   **Lifecycle Persistence**: Validates transitions (e.g. Close position) update DB state and append status changed events to the audit trail successfully.
