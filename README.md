# Trading Bot - Advanced Telegram Signal Trading Engine

[![Build Status](https://img.shields.io/badge/.NET-10.0-blue.svg)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Database](https://img.shields.io/badge/Database-PostgreSQL-blue.svg)](https://www.postgresql.org/)

An enterprise-grade, highly resilient, and fully automated cryptocurrency trading bot. The system processes multilingual and multi-format Telegram signals (using rule-based extractors and an AI-driven LLM understanding layer), executes risk-managed leveraged trading operations via Bybit's Unified V5 API, maintains thread-safe positions with complex trailing stop-losses/take-profits, and provides an observable Next.js web dashboard with advanced metrics and report generation.

---

# Project Overview

## What is this project?
This project is an advanced, production-ready, asynchronous trading bot built on the **.NET 8.0/10.0** platform. It integrates a Telegram channel message listener, a multi-stage Signal Intelligence pipeline, a deterministic and AI-powered Parsing Engine, a high-precision Risk Management Rule Engine, a fail-closed Order Execution System, a state-managed Position Protection & Lifecycle module, a Next.js operational web dashboard, and a comprehensive Analytics & Reporting Engine.

The system is designed with a strict **Clean Architecture & Domain-Driven Design (DDD)** approach, isolating business rules from infrastructure, databases, and physical exchanges. It guarantees millisecond-level signal validation and order building, with native resilience patterns (exponential backoff, retry, circuit breakers, and state reconciliation loops) protecting live trading funds against API limits, WebSocket drops, and database connection downtime.

## Main Goals
* **Automated Signal Extraction**: Decouple raw Telegram messages from trade inputs by sanitizing, classifying, and extracting trade actions (entries, targets, SL, leverage) dynamically.
* **Fail-Closed Execution**: Prevent market orders from executing unless they meet strict risk, leverage, margin, and exchange-specific lot/tick-size rules.
* **Resilient Position Management**: Protect positions via dynamic break-even triggers, multiple take-profit targets, trailing stop-losses, and active WebSocket-to-REST synchronization.
* **100% Operational Observability**: Keep system operators informed via Telegram notifications, audit logs with automated credential scrubbing, and direct diagnostic health probes.
* **Advanced Analytics & Web Dashboard**: Expose high-precision read models and an interactive Next.js Dashboard to monitor system health, evaluate trade statistics, profit factors, equity curves, drawdown curves, and schedule reports.

## Current Features
* **Multilingual Message Classifier**: Detects trading signals, updates, cancellations, and casual channel chatter in English and Persian using custom language dictionaries and score heuristics.
* **AI Message Parser & Templates**: Standardizes signals using regular-expression-based templates (stored in the database) or triggers fallback LLM analysis with automated backoff retry strategies.
* **Modular Risk & Size Rule Engine**: Evaluates 9 isolated rules (e.g., Drawdown, Daily Loss, Margin, Max Exposure, Leverage) across Warning/Error/Critical severities in under 0.1ms.
* **Bybit V5 Execution Adapter**: Integrates with Bybit Unified Trading Accounts (Linear Perpetuals) over secure, signed HMAC-SHA256 REST requests and parallel WebSocket streams.
* **Position Protection Lifecycle**: Supports dynamic Break-even offsets, Multi-target Take Profits (TP), Trailing Stops (Fixed and Percentage), and custom partial closing reasons.
* **Deduplication & Self-Healing Loops**: Protects against concurrent duplicate signals using database unique indexes and resolves "Unknown" order states automatically via background reconciliation workers.
* **Secured API, Analytics & Next.js Dashboard**: Minimal APIs protected under custom claim-based token validation paired with a multi-stage Dockerized Next.js Dashboard for live system monitoring and QR authentication.
* **Self-Diagnostics (`doctor` mode)**: Probes internal databases, Redis networking, Bybit connectivity, Telegram authentication, and configuration safety from the command line.

## Technology Stack
* **Runtime**: .NET 8.0 & .NET 10.0 SDK
* **Frontend Dashboard**: Next.js 14, React 18, Tailwind CSS, TypeScript
* **Framework**: ASP.NET Core Minimal APIs
* **Database & ORM**: EF Core 8.0, PostgreSQL (Production), SQLite (Integration Testing)
* **Caching & Networking**: System.Net.WebSockets, HttpClient
* **Logging & Redaction**: Serilog with custom Regex-based Credential Scrubber
* **Resilience**: Polly v8 (Retry, Timeout, Jitter, Circuit Breaker)
* **Telegram Listener**: WTelegramClient (custom session and 2FA authentication)
* **Testing**: xUnit, FluentAssertions, Moq, WebApplicationFactory

---

# System Architecture

The project strictly follows **Clean Architecture** pointing dependencies inwards toward the Domain model:

```
                          TradingBot.Domain (Enterprise Domain Model)
                                       ▲
                                       │ (Referenced by)
                      TradingBot.Application (Use Cases & Service Contracts)
                           ▲                                ▲
                           │ (Implements)                   │ (Implements)
               TradingBot.Infrastructure        TradingBot.Exchange.Bybit
                (Persistence & Observability)    (REST / WS Integrations)
                           ▲                                ▲
                           │                                │
                           +---------------+----------------+
                                           │
                                   TradingBot.Worker (Runtime Host)
                                           ▲
                                           │ (Proxied API)
                                 tradingbot-dashboard (Next.js)
```

1. **TradingBot.Domain**: Contains core entities, value objects, domain events, and state machine invariants (e.g., `SignalContext`, `Order`, `Position`, `Trade`, `SystemLog`, `TradeOperation`).
2. **TradingBot.Application**: Holds application services, validator pipelines, mathematical calculators, and repository interfaces (e.g., `SignalProcessor`, `RiskRuleEngine`, `StopLossManager`, `IDashboardQueryService`).
3. **TradingBot.Infrastructure**: Coordinates cross-cutting concerns, Serilog setups, Polly policies, database persistence (`TradingDbContext`), and health checks.
4. **TradingBot.Exchange.Bybit**: Encapsulates signature generation, V5 API message framing, position list tracking, and the private/public client WebSocket handshakes.
5. **TradingBot.Parser**: Contains preprocessing, template matcher, rule-based extraction rules, and AI providers (`MockAIProvider`, prompt builders).
6. **TradingBot.Telegram**: Controls session management, update receiver loops, notification channels, and Telegram authentication paths.
7. **TradingBot.Worker**: Acts as the Composition Root bootstrapping configurations, scheduling background workers, and mapping Web host endpoints.
8. **tradingbot-dashboard**: A Next.js 14 production service built via multi-stage Docker orchestration, providing real-time operational views and status endpoints.

---

# Project Structure

```
dashboard/                           # Next.js 14 frontend web application & Dockerfile
src/
├── TradingBot.Domain/               # Core Domain models, enums, exceptions, and events
├── TradingBot.Application/          # Use cases, contract interfaces, repositories interfaces, workflow services
├── TradingBot.Parser/               # Normalizers, extractors, template system, AI classifiers and analyzers
├── TradingBot.Telegram/             # WTelegram integration, update loops, and session authorization
├── TradingBot.Exchange.Bybit/       # Bybit Unified REST client and private/public WebSocket streams
├── TradingBot.Persistence/          # Entity Framework Core DbContext, Fluent configurations, and Migrations
├── TradingBot.Infrastructure/       # Dependency injection registries, Polly policies, Serilog logging setup
└── TradingBot.Worker/               # Minimal APIs, background hosted service workers, and diagnostic CLI
tests/
├── TradingBot.UnitTests/            # Domain, calculator, parser, and risk engine unit tests
└── TradingBot.IntegrationTests/     # End-to-end integration, API endpoint tests, and high-concurrency stress tests
```

---

# Docker Compose Full-Stack Deployment

To run the complete system stack (PostgreSQL, Redis, Worker, and Dashboard):

```bash
docker compose up -d --build
```

The stack launches 4 interconnected services on the `tradingbot-network` bridge:
- `tradingbot-postgres` (PostgreSQL database on port 5432)
- `tradingbot-redis` (Redis caching on port 6379)
- `tradingbot-worker` (Backend API & background worker on port 5000)
- `tradingbot-dashboard` (Next.js Dashboard on port 3000)

Access the Next.js Dashboard at: `http://localhost:3000`

---

# Complete Trading Workflow

The full operational lifecycle of the system spans from ingestion of raw messages to position termination and performance analytics.

```
+------------------+     +-------------------+     +---------------------+
| Telegram Channel | --> |  Parser Pipeline  | --> |  Risk & Size Engine |
+------------------+     +-------------------+     +---------------------+
                                                              │
+------------------+     +-------------------+     +---------------------+
| Position Tracking| <-- | Bybit REST Order  | <-- |   Order Builder &   |
| & Protection TP/SL|    |   Execution       |     |   Validation Lot    |
+------------------+     +-------------------+     +---------------------+
       │
       ▼
+------------------+     +-------------------+     +---------------------+
| Trade Realization| --> | Dashboard Read-   | --> | Scheduled Analytics |
| & realized P&L   |     | Model Projection  |     | & Reporting Exports |
+------------------+     +-------------------+     +---------------------+
```

## Signal Processing Flow
1. **Reception**: `TelegramListenerWorker` receives updates from monitored channels, filters empty messages, and enqueues them into the thread-safe `SignalStorageQueue`.
2. **Persistence**: `SignalStorageWorker` dequeues and persists raw messages to the `TelegramMessages` table. Concurrent duplicates on `ChannelId` + `MessageId` are rejected gracefully via database unique key indices.
3. **Classification**: The `MessageClassifier` inspects keywords using localized custom rules, routing messages to Signal, TradeUpdate, Cancel, or Chat.
4. **Extraction**:
   - **Structured Mode**: Checks if any `ParserTemplate` matches the format. If yes, it parses the signal using template patterns.
   - **AI Mode**: If template matching is skipped or fails, the `AIAnalyzer` uses the template prompt configuration to format the signal via JSON schema parsing.
5. **Validation**: `SignalValidationService` guarantees that required fields (Symbol, Side, Entry, SL, TP) are populated, leverage is within legal bounds, and symbol exists in database configurations.

## Order Execution Flow
1. **Trade Decision Workflow**: Coordinates duplicate signal lookups and executes order builds transactionally in a single database unit-of-work scope.
2. **Order Building**: `OrderBuilder` canonicalizes raw symbols (e.g., `btc/usdt` -> `BTCUSDT`) and parses prices and sizes into money and decimal objects.
3. **Instrument Rules Validation**: `OrderValidator` checks the candidate order against real-time exchange constraints (minimum/maximum quantities, tick-size decimals, minimum notional values) via `IExchangeInstrumentRules` to prevent partial fills.
4. **Bybit Gateway Submission**: `TradingExecutionService` prepares a signed private REST request using HMAC-SHA256, assigns a unique idempotent ClientOrderId (`TB-{Id:N}`), and submits it to Bybit linear perpetual endpoint.
5. **State Transitions**: The order transitions: `Created` -> `Submitted` -> `Accepted` -> `Filled` / `Rejected` / `ValidationFailed`.

## Position Management Flow
1. **Creation**: Upon receiving a WebSocket "execution" update or REST fill, `PositionService` opens/updates a thread-safe `Position` entity (`Pending` -> `Open`).
2. **Protection Setup**: `StopLossManager` sets trading stops via Bybit REST API, while `TakeProfitManager` configures limit orders at the specified targets.
3. **Break-Even**: `BreakEvenManager` monitors prices and updates the Stop-Loss to entry price (plus custom offset/fees) once the trigger criteria is reached.
4. **Trailing Stops**: `TrailingStopManager` tracks trade movement, adjusting the Stop-Loss dynamically behind the price using fixed or percentage offsets.
5. **Closure**: Once a Stop-Loss is hit or all Take-Profit targets are filled, the position transitions to `Closed`, creating a permanent read-only record in the `Trades` database.

## Monitoring & Recovery Flow
1. **Continuous Health Checks**: `MonitoringWorker` schedules health checks every second, inspecting Database connectivity, Bybit REST ping, WebSocket state, and Worker heartbeats.
2. **Self-Healing Startup**: `StartupRecoveryManager` checks schemas, pending migrations, REST endpoints, and synchronizes out-of-sync database positions with Bybit's actual positions.
3. **Incomplete Operations Recovery**: `IncompleteOperationRecoveryWorker` identifies operations left in "Unknown" states due to transient API drops and resolves them safely.
4. **Graceful Shutdown**: `GracefulShutdownManager` intercepts termination signals and cleanly disconnects active WebSockets and Telegram clients within a configurable timeout.

## Analytics Flow
1. **Chronological Realization**: When a position is closed, a `Trade` record is written, capturing opening and closing prices, leverage, total volume, and fees.
2. **Query Model**: `AnalyticsQueryService` performs left-joins between `Trades` and `Positions` to project true sides and symbols without holding tracking references.
3. **Mathematical Computation**: Evaluates Gross Profit, Gross Loss, Win Rates, Average Trade Duration, Profit Factor, Drawdown Curves, and streak records chronological-by-time.
4. **Scheduled Exports**: Minimal API endpoints allow on-demand generation, caching, and scheduling of reports in CSV or JSON formats.

---

# Development Setup

## Environment Configuration
To run the project, copy the included `.env.example` file and set your credentials:

```bash
cp .env.example .env
```

Review the values in `.env` and fill them out. See [docs/configuration.md](docs/configuration.md) for a comprehensive list of all required variables.

## Database Setup
Ensure that PostgreSQL is installed and running on your system, or start the container services:

```bash
docker-compose up -d postgres redis
```

Run EF Core migrations to build the tables:

```bash
dotnet ef database update --project src/TradingBot.Persistence/TradingBot.Persistence.csproj --startup-project src/TradingBot.Worker/TradingBot.Worker.csproj
```

## Running The Project

To run the main Worker host locally:

```bash
dotnet run --project src/TradingBot.Worker/TradingBot.Worker.csproj
```

To run the Next.js Dashboard locally in development mode:

```bash
cd dashboard
npm install
npm run dev
```

The Web Host starts by default on `http://localhost:5000` (exposing APIs and health endpoints), while the Next.js Dashboard runs at `http://localhost:3000`.

## Testing

To run the complete test suite including all 800+ Unit, Integration, and Failure Simulation tests:

```bash
dotnet test src/TradingBot.sln
```

To run a specific test suite (e.g. Unit Tests):

```bash
dotnet test tests/TradingBot.UnitTests/TradingBot.UnitTests.csproj
```

---

# Project Roadmap

* **Phase 12**: Advanced Grid Trading & Multi-Asset Portfolio Balancing.
* **Phase 13**: Native Machine Learning models for predictive sentiment classification on Telegram feeds.
* **Phase 14**: Dynamic multi-exchange routing (Bybit, Binance, OKX) with real-time order-book arb execution.
* **Phase 15**: Fully customizable web dashboard with interactive charts and alerts configurations.

---

# Security Notes

* **Credential Redaction**: `EventSanitizer` and `SystemLog` sanitizers parse log payloads dynamically, wiping API Keys, Secrets, Bearer tokens, passwords, and Telegram tokens.
* **Database Encryption**: Sensitive data (such as API credentials or 2FA hashes) are protected with AES-256 encryption using the system `Security__EncryptionKey`.
* **No Hardcoded Secrets**: Secrets are always injected via environment configurations or secure files, never committed directly to source control.

---

# Contribution Guide

1. **Fork the Repository** and create a feature branch (`feature/your-awesome-feature`).
2. **Follow Coding Conventions**: Standard C# guidelines with nullable reference types enabled.
3. **Write Unit Tests**: Every new feature or bug fix must be covered by comprehensive tests.
4. **Ensure Diagnostic Pass**: Run the `tradingbot doctor` CLI command locally before submitting PRs.
5. **Open a Pull Request** describing the changes and referencing any related issues.
