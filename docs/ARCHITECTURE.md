# TradingBot - Architecture Specification
## Telegram Signal Trading Bot

This document describes the foundational Clean Architecture design, component responsibilities, database mappings, stream handlers, and resilience strategies implemented for the **Telegram Signal Trading Bot**.

---

## 1. Clean Architecture Overview

The system is built on **Clean Architecture** principles to enforce domain independence, business logic isolation, infrastructure separation, and high testability.

### Dependency Direction

The core rule of Clean Architecture is that **dependencies only point inwards**:

```
           TradingBot.Domain (Central Domain)
                        ▲
                        │ (Referenced by)
       TradingBot.Application (Use Cases & Contracts)
            ▲                                ▲
            │ (Implements)                   │ (Implements)
TradingBot.Infrastructure        TradingBot.Exchange.Bybit
 (Persistence & Cross-cutting)    (Exchange REST & WebSocket)
            ▲                                ▲
            │                                │
            +---------------+----------------+
                            │
                    TradingBot.Worker (Runtime Host)
```

- **Domain** is completely independent.
- **Application** only references **Domain**.
- **Infrastructure** and **Exchange.Bybit** implement interfaces defined in **Application**.
- **Worker** acts as the composition root, wiring up all dependencies via Dependency Injection (DI) and hosting the runtime.

---

## 2. Layer Responsibilities

### 2.1 TradingBot.Domain
- **Role**: Contains enterprise entities, value objects, domain enums, domain exceptions, and business rules.
- **Components**:
  - `Signal`: Validates incoming trading signals (symbol, action type, price, quantity).
  - `Order`: Enforces trade state transitions (e.g., preventing state changes on closed or cancelled orders) using an internal state machine.
  - `Trade`: Represents a filled transaction.
  - `Value Objects`: `Symbol`, `Quantity`, and `Money` enforce mathematical constraints (e.g. strict positive sizes, non-negative amounts) on creation.
  - `DomainException`: Central exception model for domain-specific business rule violations.

### 2.2 TradingBot.Application
- **Role**: Coordinates business workflows, defines contracts (interfaces) for external providers, and manages application use cases.
- **Components**:
  - `ISignalProcessor` / `SignalProcessor`: Coordinates receiving a signal, saving it, placing a corresponding order, and dispatching to exchange.
  - `IOrderService` / `OrderService`: Manages order creation workflows transactionalized with the exchange using a Unit of Work.
  - `IExchangeClient`: Abstract contract for exchange REST operations (e.g., place order, get status, ping).
  - `IExchangeStreamClient`: Abstraction for starting/stopping the private and public live stream connections.
  - `IMarketStream`, `IOrderStream`, `IPositionStream`: Stream channel buffers utilized to consume event feeds asynchronously via `IAsyncEnumerable`.
  - `ISignalRepository`, `IOrderRepository`, `ITradeRepository`: Abstractions for persistence.
  - `IUnitOfWork`: Abstraction for coordinating database transaction boundaries.

### 2.3 TradingBot.Infrastructure
- **Role**: Provides implementations for database context, persistence foundations, Polly resilience strategies, security utilities, and logging configurations.
- **Components**:
  - `TradingBotDbContext`: PostgreSQL EF Core context mapping domains to physical schemas.
  - `UnitOfWork` / `OrderRepository` / `TradeRepository` / `SignalRepository`: Production implementation of database access using Postgres.
  - `InMemoryRepositories`: In-memory thread-safe fallbacks for mock profiles.
  - `ResilienceService`: Holds Polly resilience pipelines for both REST (Timeout -> Retry with backoff/jitter on HTTP 429/Transient -> Circuit Breaker) and WebSockets.
  - `DatabaseHealthCheck`, `ExchangeHealthCheck`, `WebSocketHealthCheck`: Implements ASP.NET Core Health Checks.

### 2.4 TradingBot.Exchange.Bybit
- **Role**: Isolated exchange provider module implementing Bybit V5 endpoints.
- **Components**:
  - `BybitExchangeClient`: Communicates with Bybit V5 REST endpoints, signing queries using HMAC SHA-256 signatures passed through `X-BAPI` headers.
  - `BybitWebSocketClient`: Manages concurrent public and private `ClientWebSocket` handshakes.
  - `SubscriptionManager`: Thread-safe collection tracking active subscriptions to restore on reconnects.
  - `MessageHandler`: Decodes incoming JSON frames, filters heartbeats, and pushes updates to active streams.

### 2.5 TradingBot.Worker
- **Role**: Serves as the Application Entrypoint (Composition Root), background execution lifecycle manager, and web host.
- **Components**:
  - `Program.cs`: Bootstraps configurations, configures Serilog, and exposes the `/health` endpoint.
  - `ConnectionMonitorService`: Manages WebSocket client lifecycle, establishing connection on startup and disconnecting on shutdown.
  - `MarketDataBackgroundService`: Non-blocking worker consuming and logging public ticker updates.
  - `OrderSyncBackgroundService`: Non-blocking worker consuming private order execution reports and updating relational DB transactions.

---

## 3. Database Persistence Schema

The PostgreSQL schema is structured precisely around domain invariants:
- **Value Objects Mapping**: Value objects (`Symbol`, `Quantity`, `Money`) are mapped directly to table columns (e.g. `PriceAmount`, `PriceCurrency`) using EF Core owned entity configurations.
- **Indexes**: Indexed columns (`ClientOrderId`, `Symbol`, `Status`) guarantee high-efficiency operations during real-time order lookups.
- **Migrations**: EF Core Migrations are fully automated, ensuring schemas are synchronized with domain changes smoothly.

---

## 4. Resilience Strategy (Polly)

Resilience pipelines protect the bot against external networking failures:
- **REST requests**: Timeout (10 seconds) -> Exponential Backoff Jitter Retry (3 attempts on transient/429 limits) -> Circuit Breaker (opens if 50% fail within 10s window).
- **WebSockets**: Handshake timeout (15s) and automatic exponential reconnection with backoff if a socket drop is detected.
