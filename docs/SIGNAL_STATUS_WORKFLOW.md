# Signal Status Workflow & Processing Lifecycle

This document explains the lifecycle of a Telegram Signal as it flows from a raw received message through parsing, validation, and execution.

---

## 1. Signal Status Lifecycle

The system tracks every stage of signal processing. The `SignalStatus` enum encapsulates this lifecycle:

```text
Received ──> Parsing ──> Parsed ──> Validated ──> ReadyForRiskEngine ──> Executed
   │           │
   │           └───> Rejected
   └───> Rejected (if parser fails completely)
```

### Detailed Transition Rules:
1. **Received:** Initial status when a raw message is stored in the database.
2. **Parsing:** Status changed programmatically before initiating the parsing pipeline (`MarkParsing`).
3. **Parsed:** Transitioned once signal components (symbol, side, entry price, stop loss, take profits, leverage) are successfully extracted from the message (`MarkParsed`).
4. **Validated:** Status set when the validation engine evaluates the signal and all rules pass (`MarkValidated`).
5. **ReadyForRiskEngine:** Status applied after validation success, indicating that the signal is ready for risk assessment and position sizing (`MarkReadyForRiskEngine`).
6. **Rejected:** Applied if the parsing pipeline fails or if any validation rules are broken (`MarkRejected`).
7. **Executed:** Final status after the signal passes risk rules and results in real orders executed on Bybit (`MarkExecuted`).

---

## 2. Database State Update

The validation engine records details of every validation run. The following database fields on the `Signals` table are updated and persisted at the end of validation:

- `Status`: Set to `ReadyForRiskEngine` or `Rejected`.
- `ValidationStatus`: High-level category string: `Validated`, `Rejected`, or `RequiresReview`.
- `ValidationMessage`: Aggregated error and warning messages from the validation execution context (joined by semicolons).
- `ParserVersion`: Records the version of the parser/validation engine used.
- `ValidatedAt`: Timestamp indicating exactly when validation took place.
- `UpdatedAt`: Automatic EF-tracked timestamp indicating the last database change.

> **Important Constraint:** Under no circumstances is the original `RawMessage` modified or overwritten, ensuring a clear audit trail.

---

## 3. Resilience and Error Handling

The validation engine features robust exception and error isolation boundaries:

### Invalid Parsed Signal
If the parsing engine outputs a null `ParsedSignal`, the validation engine immediately terminates processing, flags the signal status as `Rejected`, sets the DB validation category to `Rejected`, logs a warning, and returns.

### Isolated Validation Rule Execution
Individual validation rules are executed inside isolated `try-catch` blocks. If any rule encounters an unhandled exception:
1. The error is logged.
2. The exception message is appended to the validation results error list.
3. The remaining rules continue to execute independently.
4. The signal is eventually marked as `Rejected` or `RequiresReview`, preventing system crashes.

### Unexpected Errors
If a database, network, or transaction error occurs during the final phase of validation execution, the engine attempts to handle it gracefully by:
- Marking the database record `ValidationStatus` as `RequiresReview`.
- Appending the exception details to the `ValidationMessage`.
- Ensuring that background hosted workers (like `SignalStorageWorker` or `TelegramListenerWorker`) do not crash or terminate.
