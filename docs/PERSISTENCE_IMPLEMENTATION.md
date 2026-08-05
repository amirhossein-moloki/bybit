# Persistence Layer Implementation Specification

This document details the Entity Framework Core and PostgreSQL persistence layer implementation for the **Telegram Signal Trading Bot**, satisfying the architectural and functional requirements of **PHASE 02 — STAGE 02**.

---

## 1. DbContext Design (`TradingDbContext`)

The `TradingDbContext` serves as the central bridge between our Clean Domain-Driven Design (DDD) domain entities and the relational PostgreSQL backend.

### 1.1 DbSet Declarations
The context exposes public `DbSet` collections for all primary domain aggregates, entities, and supporting structures:

```csharp
public DbSet<ExchangeAccount> ExchangeAccounts { get; set; }
public DbSet<Symbol> Symbols { get; set; }
public DbSet<Signal> Signals { get; set; }
public DbSet<Order> Orders { get; set; }
public DbSet<Position> Positions { get; set; }
public DbSet<Trade> Trades { get; set; }
public DbSet<RiskRule> RiskRules { get; set; }
public DbSet<SystemLog> SystemLogs { get; set; }
```

### 1.2 Fluent API & Decoupled Configuration
To avoid polluting our DDD model with DB annotations, all entity-to-table mappings are fully implemented using the `IEntityTypeConfiguration<T>` interface under the `Configurations/` namespace. This keeps the Domain project entirely decoupled from Entity Framework Core.

### 1.3 Automatic Audit & Timestamp Control
To guarantee absolute reliability and compliance across financial asset operations, we override the `SaveChangesAsync` and `SaveChanges` methods. This interceptor logic automatically sets the `CreatedAt` audit field on insert and updates the `UpdatedAt` field on update, supporting both concrete domain properties and database shadow properties. All timestamps are forced to UTC.

---

## 2. Entity Mapping Details

### 2.1 ExchangeAccounts
*   **Table:** `ExchangeAccounts`
*   **API Key Fields:** Mapped `EncryptedApiKey` -> `ApiKeyEncrypted` and `EncryptedSecret` -> `SecretEncrypted` with a maximum validation length of 500 characters and mandatory `NOT NULL` constraints.

### 2.2 Symbols
*   **Table:** `Symbols`
*   **Financial Precision:** Precise step limits mapped using `NUMERIC(18,8)` for `TickSize`, `QuantityStep`, and `MinQuantity`.

### 2.3 Signals
*   **Table:** `Signals`
*   **Large Text Support:** Mapped `RawMessage` to PostgreSQL `TEXT` data type to support large incoming Telegram payloads.
*   **Precision:** Financial target triggers mapped using `NUMERIC(18,8)`.
*   **Indexes:** Optimized queries on `Symbol`, `Status`, and `CreatedAt` to support lightning-fast signal parsing, validation, and historical queries.

### 2.4 Orders
*   **Table:** `Orders`
*   **Value Object Mapping:** Embedded Owned Types (`Symbol`, `Quantity`, `Price`) mapped cleanly to columns (`Symbol`, `Quantity`, `QuantityUnit`, `Price`, `PriceCurrency`) to retain DDD purity.
*   **Precision:** Precise values mapped using `NUMERIC(18,8)`.
*   **Indexes:** Unique index on `ClientOrderId` to guarantee transaction idempotency. Indexes on `ExchangeOrderId` and composite `(Status, CreatedAt)` to optimize WebSocket event processing loops.

### 2.5 Positions
*   **Table:** `Positions`
*   **Precision:** `EntryPrice`, `CurrentPrice`, and `UnrealizedPnL` mapped using `NUMERIC(18,8)`.
*   **Indexes:** Composite index on `(Symbol, Status)` to fetch active positions dynamically during real-time market stream ticks.

### 2.6 Trades
*   **Table:** `Trades`
*   **Precision:** Precise monetary metrics mapped using `NUMERIC(18,8)` for `EntryPrice`, `ExitPrice`, `ProfitLoss`, and `Fee`.
*   **Indexes:** Query optimization on `PositionId` and `TradeId` to resolve WebSocket execution reports.

### 2.7 Delete Behaviors
To prevent accidental cascading deletion of critical financial logs, orders, or positions, all foreign-key relationships enforce `DeleteBehavior.Restrict`.

---

## 3. Database Configurations & Environment Setup

### 3.1 Connection String Registration
In order to connect to the database, specify the connection string in the `ConnectionStrings` section of your configuration (or utilize fallback settings):

```json
{
  "ConnectionStrings": {
    "TradingDatabase": "Host=localhost;Database=tradingbot;Username=postgres;Password=YOUR_SECURE_PASSWORD"
  }
}
```

### 3.2 Dependency Injection
Register the persistence container within your application startup:

```csharp
services.AddPersistence(configuration);
```

This registers `TradingDbContext` configured with the PostgreSQL provider (using `Npgsql`) and targets the migrations assembly of `TradingBot.Persistence`.

---

## 4. Migration & Schema Creation Process

### 4.1 CLI Setup
To perform migrations, ensure the `dotnet-ef` tool is installed:

```bash
dotnet tool install --global dotnet-ef --version 8.0.11
```

### 4.2 Adding a Migration
To add a new database migration, run:

```bash
dotnet ef migrations add <MigrationName> \
  --project src/TradingBot.Persistence/TradingBot.Persistence.csproj \
  --startup-project src/TradingBot.Worker/TradingBot.Worker.csproj \
  --context TradingDbContext \
  --output-dir Migrations
```

### 4.3 Applying Migrations Programmatically on Startup
Migrations are safely applied on worker startup inside `Program.cs`. When starting, the host checks whether the context points to a relational provider (such as PostgreSQL) and automatically runs:

```csharp
await context.Database.MigrateAsync();
```

This ensures zero-downtime, safe schema setup, with precise logs indicating progress and completion.

---

## 5. Development Seed Data

To accelerate development and local sandbox testing, the database startup routine includes a programmatic seeder (`DatabaseSeeder.cs`) that verifies and loads basic developer seed data:
*   **Default Symbols:** `BTCUSDT` and `ETHUSDT` (populated with standard tick size, min quantity, and quantity steps).
*   **Default Risk Configuration:** Standard risk configuration profile (2.0% maximum risk percent per trade, maximum of 5 open positions, maximum daily loss of $1000, and maximum leverage of 10x).

Seeding is safe and idempotent, preventing any duplicate insertions on subsequent application restarts. No production credentials, real API keys, or user secrets are ever seeded.
