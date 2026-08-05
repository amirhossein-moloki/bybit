# PostgreSQL Database Schema Design Specification

This document provides the complete, production-ready relational database design for the **Telegram Signal Trading Bot**. It aligns our Domain-Driven Design (DDD) model with PostgreSQL storage constraints to ensure optimal performance, complete data auditability, and absolute integrity for our financial trading transactions.

---

## 1. Global Constraints & Types

*   **Primary Keys:** All tables use globally unique `UUID` identifiers.
*   **Timestamps:** All dates and times are stored using `TIMESTAMP WITH TIME ZONE` (`timestamptz`) to prevent timezone ambiguities.
*   **Financial Precision:** All amounts, quantities, and prices use PostgreSQL `NUMERIC(18,8)` to ensure absolute accuracy with zero floating-point rounding errors.
*   **Delete Rules:** All foreign key constraints enforce `ON DELETE RESTRICT` (no cascade deletion) to preserve financial execution records, logs, and transaction audit trails.

---

## 2. Table Specifications

### 2.1 Table: `ExchangeAccounts`
Represents secure connection parameters for one or more exchange accounts.

| Column | PostgreSQL Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| `Id` | `UUID` | `PRIMARY KEY` | Unique ID of the account. |
| `ExchangeName` | `VARCHAR(50)` | `NOT NULL` | E.g. `BYBIT`, `BINANCE`. |
| `Environment` | `VARCHAR(50)` | `NOT NULL` | E.g. `mainnet`, `testnet`. |
| `EncryptedApiKey` | `TEXT` | `NOT NULL` | Encrypted API Key. |
| `EncryptedSecret` | `TEXT` | `NOT NULL` | Encrypted Private Secret Key. |
| `Status` | `VARCHAR(20)` | `NOT NULL` | Active, Inactive, Suspended. |
| `CreatedAt` | `TIMESTAMP WITH TIME ZONE` | `NOT NULL` | Account registration time. |
| `UpdatedAt` | `TIMESTAMP WITH TIME ZONE` | `NULL` | Record last updated time. |

---

### 2.2 Table: `Symbols`
Represents tradable symbol metadata and exchange limits.

| Column | PostgreSQL Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| `Id` | `UUID` | `PRIMARY KEY` | Unique ID of the symbol. |
| `Exchange` | `VARCHAR(50)` | `NOT NULL` | Target exchange. |
| `SymbolCode` | `VARCHAR(20)` | `NOT NULL` | Unique ticker code (e.g., `BTCUSDT`). |
| `BaseAsset` | `VARCHAR(10)` | `NOT NULL` | E.g. `BTC`. |
| `QuoteAsset` | `VARCHAR(10)` | `NOT NULL` | E.g. `USDT`. |
| `TickSize` | `NUMERIC(18,8)` | `NOT NULL, CHECK (TickSize > 0)` | Minimum price increment. |
| `QuantityStep` | `NUMERIC(18,8)` | `NOT NULL, CHECK (QuantityStep > 0)`| Minimum quantity increment. |
| `MinQuantity` | `NUMERIC(18,8)` | `NOT NULL, CHECK (MinQuantity > 0)` | Minimum order size limit. |
| `CreatedAt` | `TIMESTAMP WITH TIME ZONE` | `NOT NULL` | Symbol creation/import time. |

---

### 2.3 Table: `Signals`
Represents signals received from Telegram sources.

| Column | PostgreSQL Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| `Id` | `UUID` | `PRIMARY KEY` | Unique ID of the signal. |
| `Source` | `VARCHAR(100)` | `NOT NULL` | Originating channel or user. |
| `RawMessage` | `TEXT` | `NOT NULL` | Original payload text. |
| `Symbol` | `VARCHAR(20)` | `NOT NULL` | Ticker code. |
| `Side` | `VARCHAR(20)` | `NOT NULL` | `Buy` or `Sell`. |
| `EntryPrice` | `NUMERIC(18,8)` | `NOT NULL, CHECK (EntryPrice > 0)` | Intended trigger price. |
| `Quantity` | `NUMERIC(18,8)` | `NOT NULL, CHECK (Quantity > 0)` | Order execution size. |
| `StopLoss` | `NUMERIC(18,8)` | `NULL, CHECK (StopLoss > 0)` | Safety trigger price. |
| `TakeProfit` | `NUMERIC(18,8)` | `NULL, CHECK (TakeProfit > 0)` | Target exit price. |
| `Leverage` | `INT` | `NULL, CHECK (Leverage >= 1)` | Position leverage multiplier. |
| `Status` | `VARCHAR(20)` | `NOT NULL` | Received, Parsed, Validated, Rejected, Executed. |
| `CreatedAt` | `TIMESTAMP WITH TIME ZONE` | `NOT NULL` | Timestamp of reception. |

---

### 2.4 Table: `Orders`
Represents dispatch orders.

| Column | PostgreSQL Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| `Id` | `UUID` | `PRIMARY KEY` | Unique order ID. |
| `SignalId` | `UUID` | `NULL, FOREIGN KEY (Signals)` | Reference to signal, if any. |
| `ClientOrderId` | `VARCHAR(100)` | `NOT NULL, UNIQUE` | Generated client identifier. |
| `Symbol` | `VARCHAR(20)` | `NOT NULL` | E.g. `BTCUSDT`. |
| `Side` | `VARCHAR(20)` | `NOT NULL` | `Buy` or `Sell`. |
| `Type` | `VARCHAR(20)` | `NOT NULL` | `Market` or `Limit`. |
| `Quantity` | `NUMERIC(18,8)` | `NOT NULL, CHECK (Quantity > 0)` | Order quantity. |
| `QuantityUnit` | `VARCHAR(10)` | `NOT NULL` | E.g. `BTC`. |
| `Price` | `NUMERIC(18,8)` | `NOT NULL, CHECK (Price >= 0)` | Unit limit price. |
| `PriceCurrency` | `VARCHAR(10)` | `NOT NULL` | E.g. `USDT`. |
| `Status` | `VARCHAR(20)` | `NOT NULL` | State (Created, Submitted, Accepted, Filled, etc.). |
| `ExchangeOrderId` | `VARCHAR(100)` | `NULL` | Raw exchange assigned ID. |
| `CreatedAt` | `TIMESTAMP WITH TIME ZONE` | `NOT NULL` | Record initialization time. |
| `UpdatedAt` | `TIMESTAMP WITH TIME ZONE` | `NULL` | Last state transition time. |

---

### 2.5 Table: `Positions`
Represents currently active futures or margin contracts.

| Column | PostgreSQL Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| `Id` | `UUID` | `PRIMARY KEY` | Unique position ID. |
| `OrderId` | `UUID` | `NOT NULL, FOREIGN KEY (Orders)`| Triggering/Entry order. |
| `Symbol` | `VARCHAR(20)` | `NOT NULL` | Ticker symbol. |
| `Side` | `VARCHAR(20)` | `NOT NULL` | Long (`Buy`) or Short (`Sell`). |
| `EntryPrice` | `NUMERIC(18,8)` | `NOT NULL, CHECK (EntryPrice > 0)` | Position average entry. |
| `Quantity` | `NUMERIC(18,8)` | `NOT NULL, CHECK (Quantity > 0)` | Position size. |
| `StopLoss` | `NUMERIC(18,8)` | `NULL, CHECK (StopLoss > 0)` | Active stop loss value. |
| `TakeProfit` | `NUMERIC(18,8)` | `NULL, CHECK (TakeProfit > 0)` | Active take profit value. |
| `CurrentPrice` | `NUMERIC(18,8)` | `NOT NULL, CHECK (CurrentPrice > 0)`| Live market mark price. |
| `UnrealizedPnL` | `NUMERIC(18,8)` | `NOT NULL` | Calculated unrealized profit. |
| `Status` | `VARCHAR(20)` | `NOT NULL` | `Open`, `Closed`, `Liquidated`. |
| `OpenedAt` | `TIMESTAMP WITH TIME ZONE` | `NOT NULL` | Position open timestamp. |
| `ClosedAt` | `TIMESTAMP WITH TIME ZONE` | `NULL` | Position closure timestamp. |

---

### 2.6 Table: `TradeHistory`
Stores exchange transaction execution reports (individual trade fills) and completed realized closure history.

| Column | PostgreSQL Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| `Id` | `UUID` | `PRIMARY KEY` | Unique record ID. |
| `PositionId` | `UUID` | `NULL, FOREIGN KEY (Positions)`| Linked position, if any. |
| `TradeId` | `VARCHAR(100)` | `NOT NULL` | Unique identifier (from exchange or system). |
| `OrderId` | `VARCHAR(100)` | `NOT NULL` | Linked Client Order ID or empty. |
| `Symbol` | `VARCHAR(20)` | `NOT NULL` | Symbol. |
| `Side` | `VARCHAR(20)` | `NOT NULL` | Direction. |
| `Price` | `NUMERIC(18,8)` | `NOT NULL, CHECK (Price > 0)`| Execution unit price. |
| `Quantity` | `NUMERIC(18,8)` | `NOT NULL, CHECK (Quantity > 0)`| Filled quantity. |
| `Fee` | `NUMERIC(18,8)` | `NOT NULL, CHECK (Fee >= 0)` | Execution fee. |
| `FeeAsset` | `VARCHAR(10)` | `NOT NULL` | Asset currency of the fee. |
| `ExecutedAt` | `TIMESTAMP WITH TIME ZONE` | `NOT NULL` | Timestamp of execution. |
| `EntryPrice` | `NUMERIC(18,8)` | `NOT NULL` | Entry price (for position closure metrics). |
| `ExitPrice` | `NUMERIC(18,8)` | `NULL` | Exit price (for position closure metrics). |
| `ProfitLoss` | `NUMERIC(18,8)` | `NULL` | Realized Profit/Loss. |
| `ClosedAt` | `TIMESTAMP WITH TIME ZONE` | `NULL` | Position closure timestamp. |

---

### 2.7 Table: `RiskRules`
Stores configurable system thresholds.

| Column | PostgreSQL Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| `Id` | `UUID` | `PRIMARY KEY` | Unique ID. |
| `MaxRiskPercent` | `NUMERIC(5,2)` | `NOT NULL, CHECK (MaxRiskPercent >= 0 AND MaxRiskPercent <= 100)` | Maximum portfolio risk %. |
| `MaxOpenPositions`| `INT` | `NOT NULL, CHECK (MaxOpenPositions > 0)` | Maximum active positions. |
| `MaxDailyLoss` | `NUMERIC(18,8)` | `NOT NULL, CHECK (MaxDailyLoss >= 0)`| Max allowed losses per day. |
| `MaxLeverage` | `INT` | `NOT NULL, CHECK (MaxLeverage >= 1)` | Maximum leverage limit. |
| `CreatedAt` | `TIMESTAMP WITH TIME ZONE` | `NOT NULL` | Creation time of this rule set. |

---

### 2.8 Table: `SystemLogs`
Stores internal logging entries and exception stacks.

| Column | PostgreSQL Type | Constraints | Description |
| :--- | :--- | :--- | :--- |
| `Id` | `UUID` | `PRIMARY KEY` | Log UUID. |
| `Level` | `VARCHAR(20)` | `NOT NULL` | Log levels: `INFO`, `WARN`, `ERROR`, etc. |
| `Category` | `VARCHAR(100)` | `NOT NULL` | Logger category / namespace. |
| `Message` | `TEXT` | `NOT NULL` | Main message. |
| `Exception` | `TEXT` | `NULL` | Stack trace payload. |
| `CreatedAt` | `TIMESTAMP WITH TIME ZONE` | `NOT NULL` | Creation timestamp. |

---

## 3. Index Strategy

Indexes are designed to guarantee ultra-fast querying during real-time trading loops and high-concurrency event streams.

### 3.1 `Signals`
*   `IDX_Signals_Symbol`: Queries signals matching specific ticker symbols.
*   `IDX_Signals_Status`: Fast identification of active, unparsed, or parsed signals.
*   `IDX_Signals_CreatedAt`: Time-based search, sorting, and cleanup.

```sql
CREATE INDEX "IX_Signals_Symbol" ON "Signals" ("Symbol");
CREATE INDEX "IX_Signals_Status" ON "Signals" ("Status");
CREATE INDEX "IX_Signals_CreatedAt" ON "Signals" ("CreatedAt");
```

### 3.2 `Orders`
*   `UK_Orders_ClientOrderId`: Unique index to guarantee idempotency and avoid duplicate placement.
*   `IDX_Orders_ExchangeOrderId`: Essential for matching real-time websocket updates with database records.
*   `IDX_Orders_Status_CreatedAt`: Optimizes status transition tracking and diagnostic lookups.

```sql
CREATE UNIQUE INDEX "IX_Orders_ClientOrderId" ON "Orders" ("ClientOrderId");
CREATE INDEX "IX_Orders_ExchangeOrderId" ON "Orders" ("ExchangeOrderId");
CREATE INDEX "IX_Orders_Status_CreatedAt" ON "Orders" ("Status", "CreatedAt");
```

### 3.3 `Positions`
*   `IDX_Positions_Symbol_Status`: Rapid retrieval of open positions during real-time price tick updates.

```sql
CREATE INDEX "IX_Positions_Symbol_Status" ON "Positions" ("Symbol", "Status");
```

### 3.4 `TradeHistory`
*   `IDX_TradeHistory_PositionId`: Speeds up position closure/metric joins.
*   `IDX_TradeHistory_TradeId`: Fast lookup of execution report duplicates.

```sql
CREATE INDEX "IX_TradeHistory_PositionId" ON "TradeHistory" ("PositionId");
CREATE INDEX "IX_TradeHistory_TradeId" ON "TradeHistory" ("TradeId");
```

---

## 4. Migration Plan

Since this system handles real-time assets, our migration strategy prioritizes **zero-downtime** execution:

1.  **Stage 01: Backward Compatible Domain Alignment (Completed):** Align C# domain models to fit new properties and tables, ensuring existing integration tests compile and pass perfectly with fallback defaults.
2.  **Stage 02: Schema Generation (Next Stage):** Use EF Core migrations to generate SQL scripts.
3.  **Stage 03: Phased Deployment:**
    *   Deploy new tables (`ExchangeAccounts`, `Symbols`, `Positions`, `RiskRules`, `SystemLogs`).
    *   Apply incremental updates to existing tables (`Signals`, `Orders`, `TradeHistory`) as non-breaking columns with safe defaults.
    *   Execute dry runs on staging before moving to production.
