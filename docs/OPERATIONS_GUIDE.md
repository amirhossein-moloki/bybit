# Operations & Monitoring Guide
## Phase 01 — Bybit Integration Core

This operations guide is compiled to help DevOps engineers, system administrators, and developers monitor, troubleshoot, and run the Telegram Signal Trading Bot in production.

---

## 1. Application Metrics & Monitoring

### 1.1 Health Endpoint
The application exposes a standard HTTP `/health` check endpoint:
- **Port**: `8080` (or as configured in ASP.NET Core environments).
- **Endpoint**: `/health`
- **Output**: Returns string `Healthy` on 200 OK.
- **Failures**: Returns `Unhealthy` on 503 Service Unavailable if critical infrastructure checks fail.

### 1.2 Structured Logs
All operational logs are routed through **Serilog** and formatted as structured text or JSON:
- **Console Log**: Standard output stream (stdout) captured by Docker or systemd journal.
- **Log Files**: Written to path configured via `Logging:LogFilePath` (default `logs/tradingbot.log`) with daily rolling file providers.

---

## 2. Common Failures & Mitigation

### 2.1 Database Unreachable
- **Symptom**: Critical log entries: `Database health check failed` or `Npgsql.PostgresException: Connection refused`.
- **Root Cause**: PostgreSQL database is offline, network firewall is blocking the port, or container is booting out of sequence.
- **Mitigation**:
  1. Verify the state of the Postgres container: `docker-compose ps`.
  2. Check the Npgsql connection string credentials.
  3. The bot utilizes EF Core automatic retries for transient connection drops, but if the database remains unreachable permanently, the worker service exits, prompting systemd/Docker to automatically restart the container.

### 2.2 WebSocket Connection Drop
- **Symptom**: Warnings in log: `WebSocket: Disconnection detected. Initiating automatic reconnect...`
- **Root Cause**: General internet drop, Bybit server maintenance, or network routing shift.
- **Mitigation**:
  - The client is resilient. It automatically triggers an **exponential backoff with jitter** reconnection loop.
  - Sockets will reconstruct, re-authenticate, and restore subscriptions using the `SubscriptionManager` buffer without human intervention.
  - Alert on log pattern: `Maximum reconnection attempts reached` (signifies total network failure).

### 2.3 Bybit API Rate Limiting (HTTP 429)
- **Symptom**: Log events with warning: `Bybit Private Request returned non-zero code... RetCode=10006 / Too Many Requests`.
- **Root Cause**: High-frequency commands or script loops exceeding IP limits.
- **Mitigation**:
  - The HTTP resilience pipeline automatically catches 429 events and applies the Exponential Backoff retry strategy, reducing execution rate until the limit reset window opens.
  - Avoid scaling multiple workers using the exact same API credentials.

### 2.4 Authentication Failure
- **Symptom**: Fatal logs: `WebSocket: API Key or Secret is not configured` or `ExchangeException: Bybit API Error (RetCode=10003): Invalid API Key`.
- **Root Cause**: Typo or outdated API Keys configured in env.
- **Mitigation**:
  - Re-verify keys in `.env` or Kubernetes secret manifests.
  - Ensure API Key permissions are restricted exclusively to "Read" and "Trade" with "Withdrawals" disabled.

---

## 3. Disaster Recovery

In the event of database corruption or host failure:
1. **Stop execution**: `docker-compose down`.
2. **Restore Postgres Volume**: Recover the last database backup (`pg_dump` snapshot).
3. **Redeploy**: Run `docker-compose up -d --build`. EF Core migrations will automatically synchronize the DB schema and verify entity state integrity.
