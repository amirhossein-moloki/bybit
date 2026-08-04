# Security Review & Audit
## Phase 01 — Bybit Integration Core

This document details the Security posture, policies, and audit findings of the Telegram Signal Trading Bot.

---

## 1. Secret Management Policy

**Rule**: No secrets, production credentials, database passwords, API keys, or private handshakes must ever be stored in version control (git), committed code files, docker configurations, or local development log profiles.

### Configuration Hierarchy
All settings are bound dynamically inside `TradingBot.Worker/Program.cs` via standard .NET Configuration providers:
1. `appsettings.json` (stores system-wide non-sensitive defaults, such as host parameters and routing options).
2. `appsettings.Development.json` (stores developer dummy endpoints and dummy database settings).
3. **Environment Variables** (overrides default options with production keys).

### Explicit Environment Fallbacks
To align with DevOps security standards, we have configured explicit overrides for three critical settings directly in the entrypoint `Program.cs`:
- **`BYBIT_API_KEY`**: Binds to Bybit API Key used by the `BybitExchangeClient` and `BybitWebSocketClient`.
- **`BYBIT_SECRET_KEY`**: Binds to Bybit API Private Secret, used to sign HTTP requests and authenticate WebSocket handshakes.
- **`DATABASE_CONNECTION`**: Binds to Npgsql Connection String for our PostgreSQL relational database.

These parameters must come from your Kubernetes secrets, Docker Compose env profiles, or host machine shell context.

---

## 2. API Permission Review

Bybit API credentials granted to the Trading Bot must adhere strictly to the **Principle of Least Privilege**:

### Required Permissions
- **Read / Query (Account, Positions, Order Status)**: Required to fetch current balances, query active positions, and monitor execution state of client-placed orders.
- **Trade (Order Placement, Cancellations)**: Required to place new spot or derivative positions and cancel unfilled orders on receipt of exit signals.

### Forbidden Permissions
- **Withdrawals (Disable completely)**: The bot is mathematically restricted from transferring capital out of the exchange account. Disabling withdrawals via the Bybit developer console prevents catastrophic theft even in the event of an API credential compromise.
- **Subaccount management / API key management**: The key must be restricted purely to execution.

---

## 3. Logging Security (Zero-Leak Policy)

Our logging infrastructure has been carefully audited to guarantee that sensitive information is never printed to outputs or files.

### Leak Prevention Audit
- **Handshakes & Authentication**: The `BybitExchangeClient` and `BybitWebSocketClient` compute signatures internally and pass them through standardized HTTP/WebSocket protocol layers. These parameters are never outputted to `ILogger` or written to `logs/tradingbot.log`.
- **Request Payloads**: Only metadata such as `ClientOrderId`, `Symbol`, and order dimensions (`Qty`, `Price`, `Side`) are printed. No raw Authorization or HMAC payload variables are logged.
- **Connection Strings**: Database initialization and background connection monitors print status and targets (e.g. `Host=localhost`), but never include passwords, usernames, or connection parameters.
- **Exceptions**: Stack traces and `ExchangeException` bodies are sanitized. Unhandled exceptions from third-party client components are caught in a dedicated outer boundary and logged cleanly.

---

## 4. Dependency Security

All external libraries are imported via NuGet and checked for vulnerabilities:

- **EF Core & Npgsql (v8.0.0)**: Relies on safe, parameterized queries to defend against SQL Injection attacks.
- **Polly (v8.0.0)**: Used as an in-process thread-safe resilience service with zero network listening hooks, avoiding any remote execution risks.
- **Serilog (v3.1.1)**: Formats outputs safely with context enrichment, avoiding raw interpolation risks.
