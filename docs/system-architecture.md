# System Architecture Documentation

This document describes the high-level, multi-layered architecture, data flow pipelines, asynchronous event communication, and database mapping relationships of the **Telegram Signal Trading Bot**.

---

## Component Overview

This section lists and defines each major system component, specifying its responsibilities, input/output contracts, and architectural limits.

### 1. Telegram Receiver
* **Responsibility**: Listens to raw Telegram channel events, filters out non-text updates, and asynchronously forwards payload packets into the storage queue.
* **Input**: WTelegram client protocol updates (e.g., `TL.UpdateNewChannelMessage`).
* **Output**: Normalized `TelegramMessageDto` structures sent to `ISignalStorageQueue`.
* **Dependencies**: `WTelegramClient`, `ITelegramSessionManager`, `ISignalStorageQueue`.
* **Internal Role**: Gatekeeper for incoming public channel data.
* **What it must NOT do**: It must **NOT** parse trade signals, evaluate risk, or interact with Bybit REST endpoints.

### 2. Signal Intelligence Engine
* **Responsibility**: Coordinates ingestion pre-processing, sanitizes Unicode Persian/Arabic digits, classifies messages, and orchestrates extraction workers.
* **Input**: Raw `TelegramMessage` domain entities.
* **Output**: Orchestrated transitions to classified states (`ANALYZED`, `FAILED`).
* **Dependencies**: `IMessagePreprocessor`, `IMessageClassifier`, `IMessageParser`, `IMetricsService`.
* **Internal Role**: Decouples signal classification and parsing mechanics from direct storage triggers.
* **What it must NOT do**: It must **NOT** place orders on the exchange or mutate active position records.

### 3. AI Message Understanding
* **Responsibility**: Performs deep LLM-based analysis on non-standard or unstructured Persian/English signal messages when matching templates are unavailable.
* **Input**: Context prompts, message texts, and JSON schemas.
* **Output**: High-precision `AIUnderstandingResult` containing structured properties and extraction confidence scores.
* **Dependencies**: `IAIProvider` (e.g. `MockAIProvider` or live HTTP endpoint client), `IPromptTemplateEngine`.
* **Internal Role**: Resilient fallback engine for non-template messages.
* **What it must NOT do**: It must **NOT** reject or approve trades based on risk levels; it only translates text to data.

### 4. Validation Layer
* **Responsibility**: Inspects parsed signal parameters against strict business rules (symbol existence, required TP/SL values, and leverage boundaries).
* **Input**: `ParsedSignal` properties and context.
* **Output**: Non-exceptional `SignalValidationResult` (Passed, Failed) with severity-bound error logs.
* **Dependencies**: `ISymbolRepository`, `IValidationRule` collections.
* **Internal Role**: Invariant guardian preventing corrupted entries from reaching execution or risk engines.
* **What it must NOT do**: It must **NOT** modify the original parsed content or open orders on the exchange.

### 5. Risk Engine
* **Responsibility**: Validates candidate signals against active portfolio constraints (Max Risk Per Trade, Open Positions, Max Leverage, Max Exposure, Daily Loss limits).
* **Input**: `TradeRiskContext` containing account balances, current exposure, and proposed order metrics.
* **Output**: `TradeDecision` (Approved, Rejected, NeedsManualReview) with immutable audit records stored in `SystemLogs`.
* **Dependencies**: `IRiskRuleEngine`, `IRiskRule` registries, `IPositionSizeCalculator`, `IRiskEvaluationRepository`.
* **Internal Role**: Portfolio guardian ensuring trade sizes align with active capital rules.
* **What it must NOT do**: It must **NOT** submit orders to Bybit or communicate directly with WebSocket stream clients.

### 6. Execution Engine
* **Responsibility**: Coordinates order construction, validates lot-size constraints, and routes requests to the exchange adaptor safely using decoupled transactional boundaries.
* **Input**: Approved `TradeDecision` records.
* **Output**: `ExecutionResult` and native domain `Order` structures.
* **Dependencies**: `IOrderBuilder`, `IOrderValidator`, `IExchangeTradingGateway`, `IUnitOfWork`.
* **Internal Role**: Orchestrator for transactional order submission.
* **What it must NOT do**: It must **NOT** evaluate risk engine rules or manage trailing stop-losses directly.

### 7. Bybit Integration
* **Responsibility**: Formulates HMAC-SHA256 signed REST requests, maps domain schemas to Bybit spot/perpetual parameters, and parses socket feed frames.
* **Input**: Domain objects (`OrderRequest`, `PositionTarget`).
* **Output**: Unified exchange payloads, REST response bodies, and raw event flows.
* **Dependencies**: `HttpClient`, private/public `ClientWebSocket` buffers, `SubscriptionManager`, `IResilienceService`.
* **Internal Role**: Concrete adapter to Bybit's external API layer.
* **What it must NOT do**: It must **NOT** maintain application states or calculate portfolio risk scores.

### 8. Position Manager
* **Responsibility**: Governs thread-safe position state machines, coordinates multi-target take-profits, updates break-even triggers, and calculates realized trade P&L.
* **Input**: WebSocket streams, REST polling synchronizations, and execution fills.
* **Output**: Mutated database state transitions (`Open` -> `PartiallyClosed` -> `Closed`), SL/TP order submissions.
* **Dependencies**: `IPositionRepository`, `IPositionLockManager`, `IStopLossManager`, `ITakeProfitManager`, `IBreakEvenManager`.
* **Internal Role**: State manager of the active trading portfolio.
* **What it must NOT do**: It must **NOT** classify or process raw Telegram messages.

### 9. Monitoring System
* **Responsibility**: Executes fast-tick scheduled status evaluations, aggregates connection and worker states, and manages active system alerts and metrics.
* **Input**: Registered component status ticks, memory allocations, and loop iterations.
* **Output**: In-memory status caches, detailed `/monitoring/health` payloads, and persistent `HealthCheckResult` table records.
* **Dependencies**: `IHealthCheckEngine`, `IWorkerHealthRegistry`, `IAlertEngine`, `IMetricsService`.
* **Internal Role**: Real-time diagnostic heartbeat and system status monitor.
* **What it must NOT do**: It must **NOT** block critical trade execution or position protection loops.

### 10. Notification System
* **Responsibility**: Schedules and formats outgoing notification messages, deduplicates overlapping alerts, and dispatches notices to operator channels.
* **Input**: Event streams and system alerts.
* **Output**: Executed Telegram message calls.
* **Dependencies**: `INotificationPolicy` registries, `INotificationChannel` integrations.
* **Internal Role**: External communication gateway for system events.
* **What it must NOT do**: It must **NOT** perform trade operations or mutate position data.

### 11. Reliability Layer
* **Responsibility**: Orchestrates Polly resilience pipelines (retry, timeout, jitter, and circuit breaker states) to isolate transient errors.
* **Input**: Asynchronous delegate operations (HTTP REST, WebSocket connections, database queries).
* **Output**: Executed tasks with automatic error classification and retry handling.
* **Dependencies**: Polly v8 libraries, `IErrorClassifier`, `IRetryDelayCalculator`.
* **Internal Role**: Fault isolation and connection recovery shield.
* **What it must NOT do**: It must **NOT** modify core entity states or bypass business-rule validations.

### 12. Analytics System
* **Responsibility**: Computes trade statistics, realizes win/loss metrics, generates chronological equity and drawdown curves, and exports reports.
* **Input**: Completed read-only database records.
* **Output**: Metrics payloads, Excel/CSV reporting streams, and report scheduler records.
* **Dependencies**: `IAnalyticsQueryService`, `IPerformanceAnalyticsService`, `IReportScheduleRepository`.
* **Internal Role**: Chronological trade performance evaluation engine.
* **What it must NOT do**: It must **NOT** execute active trades or change live position configurations.

---

## Data Flow

The diagram below represents the chronological processing of a trading signal, from raw Telegram ingestion to statistical analytics:

```
 Telegram Message
        ↓
 Raw Message Storage      [TelegramMessages] -> Unique Index Prevents Duplicates
        ↓
 Message Classification   [MessageClassifier] -> Signal vs. TradeUpdate vs. Cancel
        ↓
 Signal Intelligence      [Template System / AI Analyzer] -> SignalContext (RECEIVED -> PROCESSING)
        ↓
    Validation            [SignalValidationService] -> Correct decimals, symbol validation
        ↓
  Risk Evaluation         [RiskEngineService] -> 9 Protection Rules checked in parallel
        ↓
 Execution Request        [TradeExecutionOrchestrator] -> Atomically creates Order record
        ↓
   Exchange Order         [BybitExchangeClient] -> Signed REST Order Submission
        ↓
 Position Management      [PositionService / Protection Managers] -> Track position state & TP/SL
        ↓
    Trade Result          [PositionCloseManager] -> realization of Net P&L and fees
        ↓
     Analytics            [AnalyticsQueryService] -> left-join read projections for reports
```

1. **Telegram Ingestion**: Telegram message is read by `WTelegram` wrapper, normalized into `TelegramMessageDto`, and enqueued to database.
2. **Signal Intelligence**: Checked against template regex matches or falls back to `MockAIProvider` / LLM API. High-confidence results create a persistent `SignalContext`.
3. **Risk Guard**: Proposed volumes are passed through the `RiskRuleEngine`. If any critical rule is violated, status becomes `Rejected` and execution stops.
4. **Execution Adapter**: Order builder calculates precise lot sizing. Requests are sent over HMAC-signed HTTP POST to Bybit Testnet/Production.
5. **Lifecycle tracking**: Sockets track order status (`Accepted` -> `Filled`). Fills initialize a persistent `Position` record with active TP/SL brackets.
6. **Chronological Closure**: Target fill triggers automatic limit order execution on Bybit. State transitions to `Closed`, computing trade metrics and writing a read-only `Trade` entity.
7. **Reporting Pipeline**: Query queries fetch trade records using non-tracking read-only projections to feed minimal API dashboard endpoints and CSV outputs.

---

## Event Flow

The system employs a decoupled, asynchronous event architecture to process background workflows without stalling execution loops:

```
+-----------------+                      +---------------------------+
| Event Producers | --(Enqueue Event)--> | IMonitoringEventQueue     |
| (Workers, REST) |                      | (System.Threading.Channel)|
+-----------------+                      +---------------------------+
                                                       │
                                                       ▼
+-----------------+                      +---------------------------+
| Alert Engine    | <--(Intercept Alerts)| MonitoringEventProcessor  |
| (Suppression)   |                      | (Background Worker)       |
+-----------------+                      +---------------------------+
                                                       │
                                                       ▼
+---------------------+                  +---------------------------+
| NotificationEngine  | <--(Dispatch)--- | DB Persistent Storage     |
| (Telegram channel)  |                  | [MonitoringEvents]        |
+---------------------+                  +---------------------------+
```

### 1. Core System Events
* **`SignalIntelligenceCreated`**: Triggered when a new signal context is successfully parsed and validated.
* **`TradeUpdateDetected`**: Emitted when a channel broadcast modifies entries or exits of an existing signal.
* **`OrderFilled` / `OrderRejected`**: Fired upon receipt of execution responses from Bybit REST or WebSockets.
* **`RateLimitDetected`**: Published when HTTP 429 is encountered, triggering circuit breakers and temporary backoffs.

### 2. Event Producers & Consumers
* **Producers**: `TelegramListenerWorker` (messages), `BybitWebSocketClient` (executions), `MonitoringWorker` (health statuses), `PositionService` (closes/SL).
* **Consumers**: `MonitoringEventProcessor` (persists and filters), `AlertEngine` (evaluates triggers/suppressions), `NotificationWorker` (formats and forwards to channels).

### 3. Correlation Flow & Error Handling
* **Correlation ID**: Every HTTP request and log entry carries an `X-Correlation-ID` header.
* **Transaction Decoupling**: Database events are published *outside* of write transactions to prevent DB locks during remote network dispatches.
* **Decoupling Resiliency**: Failure to persist an event in `MonitoringEvents` never throws a critical workflow exception. The loop logs the failure and continues executing safely.

---

## Database Flow

The following schema maps the database relationship transitions as an operation progresses:

```
   [TelegramMessages]
           │ (1-to-1 / 1-to-Many)
           ▼
    [SignalContexts] ─────────► [FailedMessageAnalysis] (on Parse Error)
           │
           ▼
     [RiskProfiles] ──────────► [RiskEvaluations] (Immutable Audit Logs)
           │
           ▼
    [TradeDecisions]
           │
           ▼
       [Orders] ──────────────► [OrderEvents] (Append-only state logs)
           │
           ▼
      [Positions] ────────────► [StopLossHistories] (Protection Trail)
           │ (1-to-1 Unique constraint)
           ▼
        [Trades] ─────────────► [Realized P&L Analytics]
```

### Relationships and Schema Purpose
1. **`TelegramMessages`**: Stores immutable raw channel messages. Ensures historical integrity and supports duplicate parsing replays.
2. **`SignalContexts`**: Tracks the extraction and state machine of parsed messages (`RECEIVED` -> `PROCESSING` -> `ANALYZED` -> `VALIDATED` -> `FAILED`).
3. **`RiskEvaluations` / `TradeDecisions`**: Maintains permanent risk auditing records mapping why a specific candidate signal was approved or rejected by the `RiskRuleEngine`.
4. **`Orders` / `OrderEvents`**: Implements audit trails of exchange orders, capturing transition timestamps for high-precision latency calculations.
5. **`Positions` / `StopLossHistories`**: Stores current market exposures, trailing states, and a detailed audit log of stop-loss alterations.
6. **`Trades`**: Relates closed positions to realized trade outputs. Realized data left-joins with positions to calculate high-performance metrics.
