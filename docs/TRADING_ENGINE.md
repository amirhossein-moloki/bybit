# Trading Engine Core Specification (Stage 03)

This document describes the core design, state transitions, persistence model, and future extensibility points implemented for the **Trading Engine Core** in Stage 03.

---

## 1. Trading Domain & Value Objects

The central domain model enforces integrity, validation, and decoupling of business concerns from infrastructure details.

### 1.1 Value Objects

The system leverages DDD value objects implemented as immutable C# records, providing structural validation at the point of instantiation:

- **`Symbol`**: Responsible for validating and normalizing symbol strings (e.g., `"BTCUSDT"`). It ensures symbols are non-empty and at least 3 characters long.
- **`Quantity`**: Encapsulates trade quantity and validates that the amount is strictly positive (`> 0`). It can optionally track the asset/unit (e.g., `"BTC"`).
- **`Money`**: Handles monetary/price representations. It validates that prices/amounts are non-negative (`>= 0`) and specifies the quote currency (e.g., `"USDT"`).

### 1.2 Enums

- **`OrderSide`**: `Buy`, `Sell`
- **`OrderType`**: `Market`, `Limit`
- **`OrderStatus`**: `Created`, `Submitted`, `Accepted`, `PartiallyFilled`, `Filled`, `Cancelled`, `Rejected`

---

## 2. Order State Machine

The order lifecycle is strictly controlled via encapsulated state transition logic inside the `Order` entity. Direct status mutations are prohibited, and any invalid state transition throws a `DomainException`.

### 2.1 State Transitions Flow

```
     Created
        │
        ▼
    Submitted
     ┌──┴────────┐
     ▼           ▼
  Accepted    Rejected
  ┌──┼────────┐
  ▼  ▼        ▼
Filled  PartiallyFilled  Cancelled
```

#### Valid Transitions:
- **`Created` → `Submitted`**: Triggered via `Submit()`. Represents that the order is being dispatched to the exchange.
- **`Submitted` → `Accepted`**: Triggered via `Accept(exchangeOrderId)`. Occurs when the exchange successfully processes and registers the order, assigning it an external ID.
- **`Submitted` → `Rejected`**: Triggered via `Reject(reason)`. Happens if exchange submission fails.
- **`Accepted` → `PartiallyFilled`**: Triggered via `MarkPartiallyFilled()`.
- **`Accepted` / `PartiallyFilled` → `Filled`**: Triggered via `MarkFilled()`.
- **`Accepted` / `PartiallyFilled` → `Cancelled`**: Triggered via `Cancel()`.

*Note: Any other transition or moving out of terminal states (`Filled`, `Cancelled`, `Rejected`) is invalid and throws a `DomainException`.*

---

## 3. Application Services & Workflows

### 3.1 `IOrderService` and `OrderService`

The application service coordinates the workflow of creating, placing, and managing orders.

- **`CreateOrderAsync`**:
  - Validates input.
  - Instantiates the domain `Order` (status starts at `Created`).
  - Saves the pending order in the repository inside a transaction.
  - Submits the order locally (`Submitted`) and saves.
  - Sends the order to the exchange using the decoupled `IExchangeClient`.
  - On successful exchange placement, accepts the order (`Accepted`) with the exchange-assigned ID.
  - On exchange failure, catches the exception, transition order state to `Rejected` locally, and commits the transaction to keep database state fully consistent and transparent.

### 3.2 Unit Of Work & Transaction Management

We leverage the **Unit of Work** pattern (`IUnitOfWork`) to guarantee atomicity of our database operations.
- Atomicity is enforced across order status transitions, ensuring that no orders are left in an inconsistent "limbo" state if any phase of the workflow fails.
- Rollback support is fully integrated. If database commands fail during a transaction, the entire sequence is completely rolled back to avoid database corruption or mismatch with exchange status.

---

## 4. Database Persistence Design

Temporary in-memory stores have been replaced with a real, enterprise-ready persistence layer powered by **PostgreSQL** and **Entity Framework Core**.

### 4.1 Schema Mappings

- **`Orders` Table**:
  - `Id` (Guid, Primary Key)
  - `ClientOrderId` (varchar)
  - `ExchangeOrderId` (varchar, Nullable)
  - `Symbol` (varchar, mapped from `Symbol` value object value)
  - `Side` (varchar, mapped from `OrderSide` enum)
  - `Type` (varchar, mapped from `OrderType` enum)
  - `Quantity` (numeric, mapped from `Quantity` value object value)
  - `QuantityUnit` (varchar)
  - `Price` (numeric, mapped from `Money` value object amount)
  - `PriceCurrency` (varchar)
  - `Status` (varchar, mapped from `OrderStatus` enum)
  - `CreatedAt` (timestamp)
  - `UpdatedAt` (timestamp, Nullable)

- **`TradeHistory` Table**:
  - `Id` (Guid, Primary Key)
  - `TradeId` (varchar)
  - `OrderId` (varchar)
  - `Symbol` (varchar)
  - `Side` (varchar)
  - `ExecutionPrice` (numeric)
  - `ExecutionQuantity` (numeric)
  - `Fee` (numeric)
  - `FeeAsset` (varchar)
  - `ExecutedAt` (timestamp)

- **`Signals` Table**:
  - `Id` (Guid, Primary Key)
  - `Symbol` (varchar)
  - `Type` (varchar)
  - `Price` (numeric)
  - `Quantity` (numeric)
  - `CreatedAt` (timestamp)

---

## 5. Future Strategy Integration

The decouplings introduced in Stage 03 provide clean extension points for subsequent stages:

1. **Strategy Subscription**:
   Strategies can interact directly with the high-level `IOrderService` to create/cancel orders using unified side, type, and quantity primitives.
2. **Order Event Stream**:
   The `Order` state machine transitions can emit domain events (e.g., `OrderFilledEvent`, `OrderCancelledEvent`) to notify strategy engines of execution status in real-time.
3. **Database History Queries**:
   Strategies can query past executions via the `IOrderRepository` and `ITradeRepository` to calculate statistics, performance, drawdowns, and execute risk management algorithms.
