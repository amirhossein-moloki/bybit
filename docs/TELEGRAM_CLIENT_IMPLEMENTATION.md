# Telegram Client Implementation

This document details the production-ready Telegram client implementation for the **Telegram Signal Trading Bot**, built with .NET 8 and utilizing `WTelegramClient` (v4.4.7).

---

## 1. Connection Lifecycle

The connection status is represented via the thread-safe `TelegramConnectionState` state machine, with transitions lock-guarded in `TelegramClientService`:

```
[Disconnected] ──(ConnectAsync)──> [Connecting] ──> [Connected] ──> [Authenticating] ──> [Listening]
                                                                        │
                                                                  [Reconnecting] <──(Error/Failure)
```

The thread-safe state transition ensures that the health checks and monitoring components have real-time, accurate views of the connection.

---

## 2. Authentication Workflow

Authentication is performed dynamically using programmatic and configuration boundaries:

```
First Login
    │
    ▼
Verification Code Prompt (VerificationCodeProvider callback / Env Override)
    │
    ▼
Password Prompt (2FA Support - PasswordProvider / Env Override)
    │
    ▼
Encrypted Session Created on Disk (AES-256 via IEncryptionService)
    │
    ▼
Future Session Restore (Automatic Session Reuse)
```

1. **Automatic Session Reuse**: Upon connecting, the stream manager restores the session seamlessly from disk using standard encrypted streams.
2. **First Login**: If authentication is required, it triggers the callback code/password retrieval.

---

## 3. Reconnection and Resilience Strategy

The background listener (`TelegramListenerWorker`) manages the resilience of the Telegram client connection using an enterprise-ready Polly `ResiliencePipeline`:

- **Automatic Reconnection**: Active connection and state monitoring triggers recovery on interruptions.
- **Exponential Backoff with Jitter (Polly Retry)**: Reconnect attempts use exponential backoff starting at 2 seconds, up to a maximum of 60 seconds, with a maximum of 10 retries. During retries, the state is thread-safely changed to `Reconnecting`.
- **Active Timeout (Polly Timeout)**: Connect, Authenticate, and Listening Initialization operations are protected by a 30-second timeout policy.
- **Circuit Breaker (Polly Circuit Breaker)**: Handles transient failures gracefully with a circuit breaker that transitions to an open state if failure rates exceed 50% within a 2-minute sampling window and at least 3 attempts, preventing cascading failures or rate-limiting.
- **Graceful Shutdown**: The background worker listens for CancellationToken cancellation and safely disposes of the connection using standard client shutdown methods.

---

## 4. Update Processing Pipeline

WTelegram's `WithUpdateManager()` is used to process updates sequentially and resume missed events reliably:

1. **Channel/Group Message Filtering**: Only new messages (`UpdateNewMessage`) originating from monitored channels or groups are processed. Basic direct messages (Users), edited, deleted, reactions, or presence updates are safely ignored.
2. **Media-Only Event Elimination**: If the message body is empty or contains only media references without text, it is discarded.
3. **Dynamic Monitored Channel Filtering**: Channels are matched dynamically against configured usernames, titles, or IDs defined under `Telegram:Channels`.

---

## 5. DTO Mapping

Updates are cleanly parsed and mapped into `TelegramMessageDto` structures:

```csharp
public class TelegramMessageDto
{
    public long ChannelId { get; set; }
    public string ChannelName { get; set; } = string.Empty;
    public int MessageId { get; set; }
    public long SenderId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public bool IsChannel { get; set; }
    public bool IsGroup { get; set; }
    public string RawUpdate { get; set; } = string.Empty;
}
```
