# Configuration Documentation

This document describes every environment variable utilized by the **Telegram Signal Trading Bot**, structured across the 10 required configuration groups.

---

## 1. Application Settings

### `Application__Environment`
* **Purpose**: Sets the runtime environment state.
* **Required**: Yes
* **Example**: `Production`
* **Security**: Controls safety switches. When set to `Production`, sandbox safety checks are strictly enforced.

### `Application__BotName`
* **Purpose**: Assigns the identification tag for the bot instance.
* **Required**: Optional (Defaults to `TelegramSignalTradingBot`)
* **Example**: `ArbitragePerpBot`
* **Security**: Publicly logged but contains no secret material.

---

## 2. Database Settings

### `DB_PASSWORD`
* **Purpose**: Defines the PostgreSQL root database password used by Docker Compose.
* **Required**: Yes (for containerized deployments)
* **Example**: `MySuperSecurePassword123!`
* **Security**: **Critical**. Must be kept private and never committed to version control.

### `DATABASE_CONNECTION`
* **Purpose**: Configures the connection parameters for the EF Core relational database provider.
* **Required**: Yes
* **Example**: `Host=postgres;Database=tradingbot;Username=postgres;Password=YOUR_SECURE_PASSWORD`
* **Security**: **High**. Contains the DB password. Keep out of public logs.

---

## 3. Redis Settings

### `REDIS_HOST`
* **Purpose**: Specifies the network hostname of the Redis container or caching server.
* **Required**: Optional (Defaults to `localhost` or `redis`)
* **Example**: `redis`
* **Security**: No credentials by default, but should remain isolated within private Docker networks.

### `REDIS_PORT`
* **Purpose**: Specifies the TCP network port used to connect to Redis.
* **Required**: Optional (Defaults to `6379`)
* **Example**: `6379`
* **Security**: Ensure external public traffic is blocked on this port.

---

## 4. Telegram Settings

### `Telegram__ApiId`
* **Purpose**: Identifies your Telegram Developer API App account credentials (obtained from my.telegram.org).
* **Required**: Yes
* **Example**: `1234567`
* **Security**: Keep confidential to prevent third parties from mimicking your developer app connection footprint.

### `Telegram__ApiHash`
* **Purpose**: Authenticates your developer app secret alongside `ApiId`.
* **Required**: Yes
* **Example**: `f49ac405fa9329ecb13970b89cf53a1a`
* **Security**: **High**. Never share or expose this token publicly.

### `Telegram__PhoneNumber`
* **Purpose**: The phone number associated with the Telegram account receiving monitored channel feeds.
* **Required**: Yes
* **Example**: `+1234567890`
* **Security**: Personal identifier.

### `Telegram__SessionPath`
* **Purpose**: Specifies where WTelegram client session details are cached.
* **Required**: Optional (Defaults to `telegram.session`)
* **Example**: `telegram.session`
* **Security**: **High**. The generated session file acts as an active login token. Do not commit this file.

### `Telegram__Enabled`
* **Purpose**: Enables or disables the Telegram receiver listener worker.
* **Required**: Optional (Defaults to `true`)
* **Example**: `true`
* **Security**: Low importance.

### `Telegram__Channels`
* **Purpose**: Comma-separated list of monitored Channel IDs, usernames, or titles.
* **Required**: Yes
* **Example**: `12345678,-1001234567890`
* **Security**: Declares which channels are authorized for trading inputs.

---

## 5. Bybit Settings

### `BYBIT_API_KEY`
* **Purpose**: Authenticates queries made to Bybit's Unified Trading V5 REST API endpoints.
* **Required**: Yes (for active live or testnet trading)
* **Example**: `aN8qHjM7KLa0pQW2Zs`
* **Security**: **Critical**. Wiped automatically from logs by `EventSanitizer`.

### `BYBIT_SECRET_KEY`
* **Purpose**: Generates the SHA-256 signature hash authorizing orders and position changes.
* **Required**: Yes
* **Example**: `u89QJLaYh7810ZksSmaPq9WzMbnZfK90P`
* **Security**: **Critical**. Never commit.

### `Exchange__UseSandbox`
* **Purpose**: Routes REST and WebSockets to Bybit Testnet (`true`) instead of live production (`false`).
* **Required**: Yes
* **Example**: `true`
* **Security**: Safety guard. Keep set to `true` unless fully validated in live-trading mode.

### `Exchange__SelectedExchange`
* **Purpose**: Identifies which exchange connector is activated.
* **Required**: Optional (Defaults to `Bybit`)
* **Example**: `Bybit`
* **Security**: Minimal importance.

---

## 6. AI Provider Settings

### `Parser__AI__ProviderName`
* **Purpose**: Chooses between LLM AI analysis (`OpenAI`, `Anthropic`) or a fallback mock.
* **Required**: Optional (Defaults to `MockAI`)
* **Example**: `OpenAI`
* **Security**: Non-sensitive.

### `Parser__AI__ApiKey`
* **Purpose**: API key authenticating the AI/LLM model service.
* **Required**: Optional (Required if using a live AI analyzer)
* **Example**: `sk-proj-4927bKLa819QsjZa`
* **Security**: **High**. Secret token. Redacted from logs.

---

## 7. Monitoring Settings

### `Monitoring__HealthCheckIntervalSeconds`
* **Purpose**: How frequently database, Bybit, and WebSocket health status rules are checked.
* **Required**: Optional (Defaults to `5`)
* **Example**: `5`
* **Security**: Non-sensitive.

---

## 8. Notifications Settings

### `Notification__Enabled`
* **Purpose**: Globally toggles outbound execution and system alert delivery.
* **Required**: Optional (Defaults to `true`)
* **Example**: `true`
* **Security**: Minimal importance.

### `Notification__Telegram__ChatId`
* **Purpose**: Chat or group ID where trade execution summaries and alerts are published.
* **Required**: Yes (if Notifications are enabled)
* **Example**: `-100987654321`
* **Security**: Controls who sees the trade execution and alert reports.

---

## 9. Logging Settings

### `Logging__LogLevel`
* **Purpose**: Controls output verbosity (`Verbose`, `Debug`, `Information`, `Warning`, `Error`).
* **Required**: Optional (Defaults to `Information`)
* **Example**: `Information`
* **Security**: Setting to `Verbose` should be done with care as it might capture raw payloads (though key fields are redacted).

### `Logging__LogFilePath`
* **Purpose**: Physical file path where Serilog writes log dumps.
* **Required**: Optional (Defaults to `logs/tradingbot.log`)
* **Example**: `logs/tradingbot.log`
* **Security**: Ensure file system permissions protect this directory.

---

## 10. Security Settings

### `Security__EncryptionKey`
* **Purpose**: Symmetric encryption key used by the `IEncryptionService` to protect sensitive database records.
* **Required**: Yes
* **Example**: `SuperSecure32CharEncryptionKey!!!`
* **Security**: **Critical**. Must be exactly 32 bytes (characters) long for AES-256. Lost keys prevent the bot from reading previous encrypted records.

### `Security__AllowedTelegramChatIds`
* **Purpose**: Comma-separated list of chat IDs authorized to access administrative API endpoints.
* **Required**: Optional
* **Example**: `12345678,98765432`
* **Security**: Secondary access-control shield preventing malicious Telegram accounts from injecting trade updates.
