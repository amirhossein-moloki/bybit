# Production Deployment Guide
## Phase 01 — Bybit Integration Core

This document outlines the standard production deployment configuration, database provisioning, and Docker orchestrations for the **Telegram Signal Trading Bot**.

---

## 1. System Requirements

- **Runtime Environment**: .NET 8.0 Runtime or Docker (Engine v20.10+ / Compose v2.0+).
- **Relational Database**: PostgreSQL Database (v14 or newer).
- **Network access**: Unfiltered outbound HTTP/HTTPS access to Bybit REST V5 and WebSocket gateway endpoints (`bybit.com`, `bybit-api.com`).

---

## 2. Environment Configuration

To run in production, configure the following environment variables on your deployment host, container agent, or Kubernetes pod config:

```bash
# ------------------------------------------------------------------------------
# 1. Application Settings
# ------------------------------------------------------------------------------
export Application__Environment="Production"
export Application__BotName="TelegramSignalTradingBot"

# ------------------------------------------------------------------------------
# 2. Relational Database Settings
# ------------------------------------------------------------------------------
# High priority database connection override:
export DATABASE_CONNECTION="Host=postgres-db;Database=tradingbot;Username=postgres;Password=YOUR_SECURE_PASSWORD"

# ------------------------------------------------------------------------------
# 3. Exchange Settings
# ------------------------------------------------------------------------------
# High priority exchange overrides:
export BYBIT_API_KEY="your_production_bybit_api_key"
export BYBIT_SECRET_KEY="your_production_bybit_api_secret"

# Use false for live trading, true for testnet / demo trading
export Exchange__UseSandbox="true"
export Exchange__SelectedExchange="Bybit"

# ------------------------------------------------------------------------------
# 4. Security Settings
# ------------------------------------------------------------------------------
export Security__EncryptionKey="YOUR_SUPER_SECRET_32_CHAR_AES_KEY"
export Security__AllowedTelegramChatIds="12345678,98765432"

# ------------------------------------------------------------------------------
# 5. Logging Settings
# ------------------------------------------------------------------------------
export Logging__LogLevel="Information"
export Logging__EnableConsole="true"
export Logging__LogFilePath="logs/tradingbot.log"
```

---

## 3. Docker Deployment

A multi-stage `Dockerfile` and `docker-compose.yml` are provided in the repository root to enable rapid containerized deployment.

### 3.1 Step-by-Step Container Provisioning

1. **Clone and Navigate**:
   ```bash
   git clone https://github.com/user/tradingbot.git
   cd tradingbot
   ```

2. **Setup production `.env` file**:
   Copy `.env.example` to `.env` and fill in the production secrets:
   ```bash
   cp .env.example .env
   nano .env
   ```

3. **Deploy using Docker Compose**:
   ```bash
   docker-compose up -d --build
   ```

   This launches two services:
   - `postgres-db`: Relational database container persisting data locally.
   - `tradingbot-worker`: The C# background service host, automatically running migrations and initializing the stream clients.

4. **Verify container health**:
   You can inspect log streams and ping the health endpoint:
   ```bash
   docker-compose logs -f tradingbot-worker
   curl http://localhost:8080/health
   ```

---

## 4. Manual / Bare-Metal Deployment

If hosting directly on an operating system instance:

1. **Install .NET 8 SDK / Runtime**:
   Follow instructions at [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/8.0).

2. **Publish the Worker project**:
   ```bash
   dotnet publish src/TradingBot.Worker/TradingBot.Worker.csproj -c Release -o ./publish
   ```

3. **Configure Environment Variables**:
   Export variables to your session (or system systemd services file).

4. **Run the executable**:
   ```bash
   cd ./publish
   dotnet TradingBot.Worker.dll
   ```
