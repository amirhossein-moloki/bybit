# Troubleshooting Documentation

This document describes common operational issues, how to detect them, and their potential causes and solutions.

---

## 1. Application Issues

### Issue 1.1: Startup Failure
* **Problem**: The application process exits immediately upon launch.
* **Detection Method**: The console displays a `FATAL_STARTUP_EXCEPTION` error message, or container state is `Exited (1)`.
* **Possible Cause**: Missing critical environment settings (e.g. invalid connection strings or incomplete Bybit/Telegram tokens).
* **Solution**: Verify all variables defined in `.env` are exported correctly and check log files for missing key names.

### Issue 1.2: Configuration Errors
* **Problem**: Services fail to bind options or throw configuration validation exceptions during boot.
* **Detection Method**: Logs show `InvalidTelegramConfigurationException` or `Value cannot be null (Parameter 'apiKey')`.
* **Possible Cause**: Environmental variables are misspelled or formatted incorrectly (such as setting non-integer value to seconds thresholds).
* **Solution**: Check spelling and casing of keys inside `.env` and verify types match the structures documented in `docs/configuration.md`.

### Issue 1.3: Worker Background Crashes
* **Problem**: Background hosted services stop running while the Web Host remains online.
* **Detection Method**: The `/monitoring/health` endpoint reports the overall system status as `Degraded` or `Unhealthy`, or worker heartbeats freeze.
* **Possible Cause**: An unhandled exception was thrown inside a worker loop.
* **Solution**: Inspect the database `HealthCheckResults` table or query Serilog log files for exception stack traces to identify the failing loop.

---

## 2. Database Issues

### Issue 2.1: Connection Failure
* **Problem**: The application cannot communicate with the PostgreSQL instance.
* **Detection Method**: Logs show `Npgsql.PostgresException: Connection refused` or `SocketException: Host is unreachable`.
* **Possible Cause**: Postgres container is offline, password is wrong, or host port is blocked.
* **Solution**: Verify PostgreSQL is running with `docker-compose ps` and ensure `DATABASE_CONNECTION` matches active credentials.

### Issue 2.2: Migration Failure
* **Problem**: Run-time migrations fail to apply to the database schema.
* **Detection Method**: Startup logs show `Applying pending migrations...` followed by an error stack trace.
* **Possible Cause**: Concurrency conflicts from multiple cluster instances applying migrations simultaneously, or manual direct DB edits.
* **Solution**: Stop all application replicas, clean conflicting locks, and run `dotnet ef database update` manually from a single CLI terminal.

### Issue 2.3: Schema Mismatch
* **Problem**: SQL query errors are thrown when EF Core queries tables.
* **Detection Method**: DbUpdateException: `column "X" of relation "Y" does not exist`.
* **Possible Cause**: Code updates introduced new database tables or columns that have not been migrated to the database.
* **Solution**: Run `dotnet ef database update` to synchronize physical schemas with the active domain models.

---

## 3. Redis Issues

### Issue 3.1: Connection Problems
* **Problem**: Redis connectivity check reports FAILED.
* **Detection Method**: Command `tradingbot doctor` displays warning message: `Redis: FAILED (Connection timed out)`.
* **Possible Cause**: Redis container is not running, port 6379 is busy, or `REDIS_HOST` environment variable is misconfigured.
* **Solution**: Ensure the Redis service is online by running `docker-compose up -d redis`, and verify port binding properties.

---

## 4. Telegram Issues

### Issue 4.1: Authentication Failure
* **Problem**: WTelegramClient fails to log in to the account session.
* **Detection Method**: Logs show `TelegramAuthenticationException: Verification code is required but was not provided`.
* **Possible Cause**: First-time login requires a 2FA Password or SMS Code, but they are not present in environment configs.
* **Solution**: Provide `TELEGRAM_VERIFICATION_CODE` and `TELEGRAM_PASSWORD` temporarily in `.env` to complete the initial interactive authentication handshake.

### Issue 4.2: Message Ingestion Failures
* **Problem**: Channel posts do not appear or parse.
* **Detection Method**: Active channel broadcasts are skipped, and `SignalContext` records are not created.
* **Possible Cause**: Channel IDs are incorrect, or the authenticated Telegram account does not have read access to the monitored channel.
* **Solution**: Confirm that monitored Channel IDs (prefixed with `-100` for channels) are listed inside `Telegram__Channels`.

---

## 5. Bybit Issues

### Issue 5.1: Invalid Credentials
* **Problem**: Bybit API requests return authentication signature errors.
* **Detection Method**: HTTP response displays `RetCode=10003`, or logs show `Bybit API Error: Signature verification failed`.
* **Possible Cause**: `BYBIT_API_KEY` or `BYBIT_SECRET_KEY` are misspelled, or sandbox keys are used on the production endpoint.
* **Solution**: Re-verify keys in Bybit Account settings and confirm `Exchange__UseSandbox` matches the key's target platform.

### Issue 5.2: API Errors
* **Problem**: Orders are rejected with error response codes.
* **Detection Method**: Logs show `Bybit API Error (RetCode=10001): Account balance is insufficient`.
* **Possible Cause**: Violations of lot/tick rules, or account lacks sufficient margin collateral.
* **Solution**: Ensure your account holds sufficient USDT, and verify symbol name matches unified USDT contract specifications.

### Issue 5.3: WebSocket Disconnect
* **Problem**: WebSocket connection drops or subscriptions fail to restore.
* **Detection Method**: Logs show `WebSocket: Disconnection detected` and state transitions to `Reconnecting` or `Failed`.
* **Possible Cause**: Host network disconnect, or transient Bybit server-side load shedding.
* **Solution**: The system initiates automatic exponential backoff reconnects up to 10 attempts. If reconnection fails permanently, check host gateway internet routing.
