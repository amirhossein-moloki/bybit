# Telegram Integration Architecture

This document details the architectural foundation, security design, authentication flow, and session management strategy for the Telegram integration inside the **Telegram Signal Trading Bot** system.

---

## 1. Telegram Module Design

The Telegram integration is encapsulated within a separate, dedicated project `TradingBot.Telegram` located under `src/TradingBot.Telegram`. This isolation conforms to the principles of **Clean Architecture**, preventing leakage of third-party details into the core domain or application logic.

### Dependency Graph

```
[ TradingBot.Worker ] (Composition Root)
         │
         ├──► [ TradingBot.Telegram ] (Telegram Integration Infrastructure)
         │           │
         │           ├──► [ WTelegramClient ] (Third-party library)
         │           └──► [ TradingBot.Application ] (Core application contracts)
         │
         ├──► [ TradingBot.Infrastructure ]
         └──► [ TradingBot.Persistence ]
```

### Module Folder Structure

The structural layout of `TradingBot.Telegram` consists of:

*   **`Authentication/`**: Implements session management and the programmatic authentication flow.
    *   `TelegramSessionManager.cs`: Controls reading, writing, and deletion of the encrypted session file.
    *   `EncryptedSessionStream.cs`: A custom `MemoryStream` subclass that handles on-the-fly encryption and decryption when requested by `WTelegramClient`.
    *   `TelegramAuthService.cs`: Coordinates programmatic login, 2FA, verification code delivery, and connection states.
*   **`Client/`**: Handles connections to Telegram's MTProto servers.
    *   `TelegramClientService.cs`: Wraps `WTelegram.Client` and translates events/callbacks into structured application states.
*   **`Configuration/`**: Models the bound settings.
    *   `TelegramOptions.cs`: Custom configuration settings (ApiId, ApiHash, PhoneNumber, SessionPath, Enabled).
*   **`Interfaces/`**: System-wide contracts for loose coupling.
    *   `ITelegramClient.cs`
    *   `ITelegramAuthenticationService.cs`
    *   `ITelegramSessionManager.cs`
*   **`Models/`**: Domain models and connection state indicators.
    *   `TelegramConnectionState.cs` (Disconnected, Connecting, Connected, Authenticating, AuthenticationFailed, Error)
*   **`Exceptions/`**: Strongly-typed module-specific exceptions.
    *   `TelegramAuthenticationException`, `TelegramConnectionException`, `TelegramSessionException`, `InvalidTelegramConfigurationException`.
*   **`Health/`**: Integrated health checks for system monitoring.
    *   `TelegramHealthCheck.cs`: Exposes the current client status to the ASP.NET Core Health Checks infrastructure.

---

## 2. Programmatic Authentication Flow

Since the bot runs headlessly as a background service, the interactive CLI prompt standard in `WTelegramClient` is intercepted. We leverage programmatic delegates to handle code submission and 2FA authentication securely.

### Login & Code Sequence

```
[ TelegramAuthService ]           [ TelegramClientService ]          [ WTelegram.Client ]
         │                                   │                                │
         │──► AuthenticateAsync()            │                                │
         │                                   │                                │
         │──► ConnectAsync() ───────────────►│                                │
         │                                   │──► Create & Connect ──────────►│
         │                                   │                                │
         │──► LoginUserIfNeeded() ───────────┼───────────────────────────────►│
         │                                   │                                │
         │                                   │◄── Request "api_id"/"phone" ───│ (ConfigProvider callback)
         │                                   ├─── Supply configured values ──►│
         │                                   │                                │
         │                                   │◄── Request "verification_code" │ (Prompt code)
         │                                   ├─── Call CodeProvider delegate ─►│
         │                                   │                                │
         │◄───────────────── OK / UserLogged ┼────────────────────────────────│
```

### Delegation Fallbacks
If the `TelegramClientService` needs a dynamic code (e.g. `verification_code` or `password`), it attempts to resolve it through:
1.  **Injected Delegates**: Programmatic callers can register a synchronous provider `Func<string>` via the service properties.
2.  **Environment Variables**: As a backup (e.g., for automated environments), the system checks `TELEGRAM_VERIFICATION_CODE` and `TELEGRAM_PASSWORD`.
3.  **Exception Fallback**: If neither is present, an explicit `TelegramAuthenticationException` is thrown, preventing blockages or hanging threads.

---

## 3. Security & Secret Management

Protecting user identity and external MTProto keys is a critical requirement of the Telegram module.

### Zero Secrets in Source Code
Sensitive values, including:
*   `ApiHash`
*   `PhoneNumber`
*   `Session Files`

are **never** hardcoded or committed to git. They are managed via:
*   **Hierarchical Configuration**: Configured in `appsettings.json` or development secrets.
*   **Environment Variable Overrides**: Supported overrides like `TELEGRAM_API_ID`, `TELEGRAM_API_HASH`, and `TELEGRAM_PHONE` are loaded automatically at startup inside the composition root/dependency injection hook.

### Sanitized Redacted Logging
We follow a strict log-redaction strategy.
*   Logs will report structural events like `"Telegram connection started"`, `"Authentication successful"`, or `"Session restored"`.
*   Credentials, verification codes, raw binary session bytes, and phone numbers are strictly **forbidden** from appearing in any console or file logs.

---

## 4. Session Management Strategy

To maintain persistent logins without exposing credentials, the session file is encrypted at rest using AES-256.

### EncryptedSessionStream (Custom AES-256 Stream)
When `WTelegramClient` instantiates, we pass our custom stream `EncryptedSessionStream`:
1.  **Loading**: On load, if the session file exists, its content is read as ciphertext, decrypted using `IEncryptionService` (backed by AES-256 with a secure secret key), converted back from Base64, and written to the inner `MemoryStream`.
2.  **Saving / Intercepting**: We override the stream's writing APIs (`Write`, `WriteByte`, `SetLength`, and `Flush`). Whenever `WTelegramClient` updates the active session keys:
    *   The decrypted bytes are extracted from the memory stream.
    *   They are converted to a Base64 string.
    *   The Base64 string is encrypted via `IEncryptionService`.
    *   The encrypted string is written atomically to disk at `SessionPath`.

This ensures that **no plain-text session keys or negotiation states are ever stored on disk**.

---

## 5. Future Receiver Integration

In subsequent development stages:
*   An update coordinator background worker (e.g., `TelegramMessageReceiver`) will consume the `ITelegramClient` and register update managers using MTProto update listeners.
*   The connection monitoring background service will track the health of MTProto connection state and attempt automatic reconnects on network degradation.
*   Message filtering and signal parsing logic will be implemented as downstream Application-layer services processing incoming events from authorized channel IDs.
