# PostgreSQL Performance Optimization & Schema Integrity

This document outlines the performance optimizations, schema design strategies, and constraints enforced at the database layer to handle high volumes of trades safely and efficiently.

---

## 1. Index Strategy

Indexes have been established on high-frequency query filters to prevent full-table scans, reduce query latencies, and minimize transaction blockages.

### Orders Table
- **ExchangeOrderId**: Crucial for looking up specific orders when handling exchange WebSockets or REST synchronization.
- **Status**: High-frequency filter for open vs. closed orders.
- **Symbol**: High-frequency filter for active trading symbols.
- **CreatedAt**: Ordering index used for historical pagination and charts.
- **Composite Index (Status, CreatedAt)**: Extremely helpful for pulling ordered subsets of active order states.

### Signals Table
- **Symbol**: Filters signals for specific trading pairs.
- **Status**: Filters parsed vs. pending signals.
- **CreatedAt**: Ordering index for chronological signals history.

### Positions Table
- **Composite Index (Symbol, Status)**: Used to quickly query open vs. closed positions for active assets.

### Trades Table
- **PositionId**: Joins trades back to their parent positions.
- **ClosedAt**: Indexes historical execution trade settlement timestamps.

---

## 2. Integrity & Database Constraints

Invalid states at the application layer must be blocked by the relational database layer. We utilize Fluent API database CHECK constraints to enforce mathematical correctness:

```csharp
t.HasCheckConstraint("CK_Orders_Quantity", "\"Quantity\" > 0");
t.HasCheckConstraint("CK_Orders_Price", "\"Price\" >= 0");
```

| Table | Constraint | Enforced SQL Rule | Purpose |
|---|---|---|---|
| **Orders** | `CK_Orders_Quantity` | `"Quantity" > 0` | Prevent zero or negative volume orders |
| **Orders** | `CK_Orders_Price` | `"Price" >= 0` | Prevent negative pricing |
| **Signals** | `CK_Signals_Quantity` | `"Quantity" > 0` | Ensure signal target size is positive |
| **Signals** | `CK_Signals_EntryPrice` | `"EntryPrice" >= 0` | Ensure valid entry price limits |
| **Positions** | `CK_Positions_Quantity` | `"Quantity" > 0` | Ensure position has real volume |
| **Positions** | `CK_Positions_EntryPrice` | `"EntryPrice" >= 0`| Enforce positive position entry |
| **Trades**| `CK_Trades_Quantity` | `"Quantity" > 0` | Ensure trades are positive volume |
| **Trades**| `CK_Trades_Price` | `"Price" >= 0` | Ensure trades are positive execution price |

---

## 3. Query Optimization & Tracking

- **Tracked Operations (Writes/Updates)**: Entity Framework tracking is reserved for writes and modifications. State transition updates load entities into memory, apply changes through robust DDD state methods, and call `SaveChangesAsync()` under an active database transaction.
- **AsNoTracking (Read-Only)**: To minimize memory allocation overhead and avoid CPU-heavy tracking graph checks, all queries fetching reporting history, active order lists, or historical signals explicitly call `.AsNoTracking()`.

---

## 4. Connection Management & Pooling

- **Connection Pooling**: EF Core connection pooling is registered using `AddDbContextPool<TradingDbContext>()` instead of `AddDbContext()`. This reuses active connection context instances and drastically reduces the overhead of repeatedly opening/closing physical TCP sockets to PostgreSQL.
- **Command Timeout**: Set a maximum query execution timeout (e.g., `30 seconds`) to automatically kill rogue queries or blockages and release pooled database connections.
