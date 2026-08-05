# Domain-Driven Design (DDD) Model Alignment Specification

This document details the **Domain-Driven Design (DDD)** architecture of the **Telegram Signal Trading Bot**. It outlines the entity definitions, value objects, aggregate boundaries, state machine transition rules, and security boundaries.

---

## 1. Domain Entities & Value Objects

The domain layer is completely decoupled from any persistence, network, database, or cryptographic framework. It encapsulates strictly pure, rich business logic, constraints, and valid state transitions.

### 1.1 Value Objects (Immutable)

Value Objects are used to strongly type properties, enforce strict domain validation on creation, and prevent invalid data from propagating through the application layer.

*   **`Symbol`**
    *   *Responsibility:* Encapsulates a standardized, validated market ticker code (e.g., `BTCUSDT`).
    *   *Validation:* Must be non-empty, auto-normalized to uppercase, and at least 3 characters long.
*   **`Quantity`**
    *   *Responsibility:* Encapsulates order/execution size along with its base unit asset (e.g. `0.025 BTC`).
    *   *Validation:* Value must be strictly greater than zero. Asset unit must be non-empty and uppercase.
*   **`Money`**
    *   *Responsibility:* Encapsulates currency-denominated values (e.g., `52000.50 USDT`).
    *   *Validation:* Amount must be non-negative. Currency must be non-empty and uppercase.

---

### 1.2 Domain Entities (Rich Models)

Entities have unique identity over time, mutable state, private setters, and encapsulate state machine behaviors.

*   **`ExchangeAccount`**
    *   *Responsibility:* Models exchange authorization configurations.
    *   *Security Boundary:* Encapsulates sensitive fields as *encrypted* strings (`EncryptedApiKey`, `EncryptedSecret`). The domain is completely unaware of the encryption implementation (AES, Npgsql converters, etc.). This preserves Clean Architecture boundaries.
*   **`Symbol` (Entity)**
    *   *Responsibility:* Represents a backed config entity specifying trading parameters of an asset (e.g., `BTCUSDT` supported limits, `TickSize`, `QuantityStep`, `MinQuantity`). Used for pre-flight order size validation.
*   **`Signal`**
    *   *Responsibility:* Models a trading signal received from a signal channel.
    *   *State Machine:* `Received -> Parsed -> Validated -> Rejected -> Executed`.
    *   *Constraint:* No parsing or technical execution calculations inside the entity.
*   **`Order`**
    *   *Responsibility:* Models a dispatched order. Represents the core execution transactional root.
    *   *State Machine:* `Created -> Submitted -> Accepted -> Filled/PartiallyFilled/Cancelled/Rejected`.
    *   *Constraint:* Status transitions must occur through explicit domain methods (`Submit()`, `Accept()`, `MarkFilled()`, `Cancel()`, `Reject()`). Direct status updates are restricted.
*   **`Position`**
    *   *Responsibility:* Models an active futures/margin position.
    *   *Rich Logic:* Includes `UpdatePrice()` which auto-calculates unrealized P&L based on the direction (`Buy` vs `Sell`), and `Close()` / `Liquidate()` state handlers.
*   **`Trade`**
    *   *Responsibility:* Represents execution trade fills from the exchange, OR completed position closure execution history with realized P&L.
*   **`RiskRule`**
    *   *Responsibility:* Encapsulates risk management configurations (`MaxRiskPercent`, `MaxOpenPositions`, `MaxDailyLoss`, `MaxLeverage`).
*   **`SystemLog`**
    *   *Responsibility:* Models audit logging entries to preserve application integrity records.

---

## 2. Aggregate Boundaries

Aggregates define transaction consistency boundaries.

```
┌──────────────────────────────────────────────────────────┐
│                    Trading Aggregate                     │
│                                                          │
│     ┌─────────────┐                ┌─────────────┐       │
│     │    Order    │ 1:N            │    Trade    │       │
│     │  (Ag. Root) ├───────────────>│ (Execution) │       │
│     └─────────────┘                └─────────────┘       │
└──────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────┐
│                   Position Aggregate                     │
│                                                          │
│     ┌─────────────┐                                      │
│     │  Position   │                                      │
│     │  (Ag. Root) │                                      │
│     └─────────────┘                                      │
└──────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────┐
│                    Signal Aggregate                      │
│                                                          │
│     ┌─────────────┐                                      │
│     │   Signal    │                                      │
│     │  (Ag. Root) │                                      │
│     └─────────────┘                                      │
└──────────────────────────────────────────────────────────┘
```

### 2.1 The Trading Aggregate
*   **Root:** `Order`
*   **Ownership:** An `Order` owns its status, client identifiers, pricing parameters, and coordinates individual execution fills (`Trade` objects associated with it).
*   **Allowed Modifications:** Updates to status must respect strict transition rules. Once an order is closed (`Filled`, `Cancelled`, `Rejected`), no further state modifications are permitted.

### 2.2 The Position Aggregate
*   **Root:** `Position`
*   **Ownership:** Controls position pricing, active risk adjustments (updating `StopLoss` and `TakeProfit` thresholds), and handles final closure/liquidation states.
*   **Allowed Modifications:** Can modify stop loss, take profit, current market price, and unrealized P&L. Once in a closed terminal status, the position becomes immutable.

### 2.3 The Signal Aggregate
*   **Root:** `Signal`
*   **Ownership:** Guarantees signal integrity. It coordinates transitions between parsing, validation, execution, and rejection states.
*   **Allowed Modifications:** Modifications are only allowed through explicit lifecycle methods.

---

## 3. Security Considerations & Encapsulation

1.  **Strict Encapsulation:** All entity fields have `private set` access. Construction always goes through parameterized constructors that enforce strict business invariant validations (e.g. non-empty names, quantities > 0, prices >= 0).
2.  **No Infrastructure Bleeding:** Infrastructure concepts (such as database IDs, ORM mapping attributes, serialization rules, or encryption libraries) are kept entirely out of the domain models. For example, `ExchangeAccount` contains pure string properties for keys and secrets, requiring the application layer to decrypt them when building the Bybit API client, keeping the domain perfectly pure.
