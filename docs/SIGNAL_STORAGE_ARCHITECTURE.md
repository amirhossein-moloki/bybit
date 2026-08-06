# Signal Storage & Reliability Layer Architecture

This document describes the architecture, duplicate protection strategy, retry/resilience strategy, database interactions, and failure handling workflows of the Signal Storage & Reliability Layer implemented in Phase 03 — Stage 04.

---

## 1. Storage Workflow

The signal receiving and persistence pipeline is structured as an asynchronous producer-consumer architecture to ensure maximum throughput, zero thread-blocking on the Telegram update listener, and graceful error boundaries.

```
[ Telegram Chat Updates ]
          │ (WTelegramClient)
          ▼
[ DefaultTelegramMessageReceiver ] ──► Increments SignalsReceived Metric
          │
          ├─► [ IMessageFilter (MessageFilterService) ] (Analyzes Message)
          │
          ▼ (If qualified SignalCandidate detected)
[ ISignalStorageQueue (SignalStorageQueue) ]
          │ (Thread-Safe Channel Buffer)
          ▼
[ SignalStorageWorker ] (Background Hosted Service Loop)
          │
          ▼ (Scoped Scope per Dequeued Candidate)
[ ISignalStorageService (SignalStorageService) ]
          │
          ├──► Checks for duplicate: ISignalRepository.ExistsAsync()?
          │         ├─► [Yes] ──► Increments DuplicatesIgnored Metric ──► Drop/Ignore
          │         └─► [No]
          │
          ├──► Maps SignalCandidate to Domain Signal Entity (Positive Placeholders for Quantity/Price)
          │
          ├──► Begins Database Transaction (IUnitOfWork)
          │         ├─► ISignalRepository.SaveAsync()
          │         ├─► IUnitOfWork.SaveChangesAsync()
          │         └─► IUnitOfWork.CommitAsync()
          │
          └──► Increments SignalsStored Metric & Logs Outcome ("Signal stored")
```

---

## 2. Duplicate Strategy

Telegram can transmit the same message multiple times under poor network conditions. We implement multi-layered duplicate detection:

1. **Database Schema Unique Constraint:**
   The `Signals` database table is enhanced with a composite unique index on `(TelegramChannelId, TelegramMessageId)` inside `SignalConfiguration.cs`:
   ```csharp
   builder.HasIndex(x => new { x.TelegramChannelId, x.TelegramMessageId }).IsUnique();
   ```
   Both columns are of type `bigint` (represented as C# `long?`). For legacy non-Telegram signals, multiple `NULL` values are safely allowed by SQL standard unique constraints in both PostgreSQL and SQLite.

2. **Pre-insert Optimization Check:**
   Before commencing a write transaction, `SignalStorageService` checks if the message has already been saved via `ISignalRepository.ExistsAsync(channelId, messageId)`:
   ```csharp
   var exists = await _signalRepository.ExistsAsync(candidate.ChannelId, candidate.MessageId);
   ```
   If a duplicate is found:
   - The message is silently ignored.
   - The `DuplicatesIgnored` metric is incremented.
   - Detailed redacted auditing logs the duplicate channel and message ID:
     ```
     Duplicate signal ignored
     Channel: 12345
     MessageId: 987
     ```

---

## 3. Retry & Resilience Strategy

Relational databases can occasionally experience transient connection drops. We configure automatic retry resilience directly on theEntity Framework Core Npgsql driver inside `TradingBot.Persistence/DependencyInjection.cs`:

```csharp
options.UseNpgsql(connectionString, b =>
{
    b.MigrationsAssembly(typeof(TradingDbContext).Assembly.FullName);
    b.EnableRetryOnFailure(
        maxRetryCount: 5,
        maxRetryDelay: TimeSpan.FromSeconds(30),
        errorCodesToAdd: null);
});
```

This enables the application to automatically and transparently retry query execution and saves for connection losses, transient network splits, and PostgreSQL restarts before raising a database update failure.

---

## 4. Failure Handling

We enforce strict error boundaries to keep the system robust and online 24/7:

- **Invalid Signal Candidate:** If a candidate contains invalid fields (such as no detected symbol), it is cataloged as `Rejected` and fails fast with an exception, updating the `StorageFailures` metric.
- **Database Failure / Unique Violation:**
  If a write fails (e.g. during transaction commit or constraint violation):
  1. The transaction is rolled back via `IUnitOfWork.RollbackAsync()`.
  2. The `StorageFailures` metric is incremented.
  3. The error is logged securely without printing any private credentials.
  4. The background worker catches the exception, logs it, and continues consuming the queue. This prevents any bad or failing message from crashing the entire background processing pipeline.

---

## 5. Metrics Foundation

We track and monitor system health and reliability via four key metrics managed inside the thread-safe `ISignalStorageMetrics` singleton:

- **SignalsReceived:** Incremented whenever a raw message is received by the Telegram message listener.
- **SignalsStored:** Incremented when a signal is successfully persisted to the database.
- **DuplicatesIgnored:** Incremented when a duplicate message is detected and ignored.
- **StorageFailures:** Incremented when a storage failure or invalid candidate is encountered.
