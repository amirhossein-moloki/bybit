# Signal Parser Engine - Architecture Design

This document details the foundation architecture of the **Signal Parser Engine** implemented in **Phase 04 — Stage 01**.

---

## 1. Architectural Overview

The Signal Parser Engine is decoupled from both the Telegram message receiver (ingestion) and the execution exchange module (Bybit). It acts strictly as an in-memory data transformation service, adhering to **Clean Architecture** and **Domain-Driven Design (DDD)** principles.

```
+------------------------+
|      Raw Message       |
+------------------------+
            |
            v
+------------------------+
|     ParserContext      | (Length & Sanitization validation)
+------------------------+
            |
            v
+------------------------+
|   ISignalParser        | (Execution Coordinator)
+------------------------+
            |
            v
+------------------------+
|    IParserPipeline     | (Sequential Execution of Extractor Chain)
+------------------------+
            |
            v
+------------------------+
|  ParsedSignal Model    | (Output target populated by extractors)
+------------------------+
            |
            v
+------------------------+
|     ParserResult       | (Immutable parsed outcome)
+------------------------+
```

---

## 2. Key Components & Responsibilities

### 2.1 Interfaces

- **`ISignalParser`**: Entry point for parsing tasks. Resolves the parser execution pipeline, handles safe structured logging, and cleanly translates pipeline exceptions into failed results.
- **`IParserPipeline`**: Represents the ordered orchestration engine. Executes all registered extractors sequentially against the current parser context and builds the final parsed signal.
- **`ISignalExtractor`**: Abstract definition of a single field extractor. Future stages will implement concrete extractors (e.g., `SymbolExtractor`, `EntryPriceExtractor`, etc.) to process the raw message.

### 2.2 Models

- **`ParserContext`**: Container representing the parsing request context. It encapsulates:
  - `SignalId` (unique raw signal identifier)
  - `RawMessage` (cleaned raw string payload)
  - `SourceChannel` (identity of the source)
  - `ReceivedAt` (reception timestamp)
  - `ParserVersion` (engine traceability)
  It enforces input sanitization (trimming and removal of control/null characters) and early length validation.
- **`ParsedSignal`**: Temporary in-memory model holding the intermediate extraction properties (Symbol, Side, EntryPrice, StopLoss, TakeProfits list, Leverage, and ConfidenceScore) as they are parsed.
- **`ParserResult`**: Unified, immutable outcome pattern enclosing the execution status (`Success`), the generated `ParsedSignal`, a collection of `Errors`, and a list of diagnostic `Warnings`.

### 2.3 Exceptions

- **`ParserException`**: Base abstract/general exception for all parser-related operations.
- **`InvalidParserContextException`**: Thrown when context creation input is invalid or violates length constraints.
- **`ParserExecutionException`**: Thrown to wrap nested/unexpected pipeline errors during execution.

---

## 3. Data Flow

1. The raw message is packaged into a `ParserContext` along with signal metadata. Basic length boundaries and string validation are enforced.
2. The `ISignalParser` receives the `ParserContext`.
3. The parser delegates processing to the `IParserPipeline`.
4. The pipeline instantiates a blank `ParsedSignal` and passes it along with `ParserContext` to each registered `ISignalExtractor` sequentially.
5. Once all extractors complete, the pipeline returns the populated `ParsedSignal`.
6. `ISignalParser` wraps the signal in a successful `ParserResult`. If any errors occur, they are trapped, logged, and returned cleanly as a failed `ParserResult` to protect background services from unexpected crashes.

---

## 4. Configuration & Security Options

The parser behavior is governed by the `ParserOptions` section in `appsettings.json`:

```json
{
  "Parser": {
    "Version": "1.0",
    "MaxMessageLength": 5000
  }
}
```

- **Input Sanitization**: Control codes and null bytes `\0` are automatically stripped during context construction to prevent parsing/processing exploitation.
- **Length Constraint Enforcement**: To prevent Denial of Service (DoS) attacks via extremely large message payloads, message size limits are validated at multiple stages (both context instantiation and pipeline invocation).

---

## 5. Extractor Integration (Phase 04 — Stage 02)

To add extraction capabilities in subsequent stages:
1. Implement `ISignalExtractor` for the target field (e.g., `class SymbolExtractor : ISignalExtractor`).
2. Implement extraction rules (regex, keywords, AI assistance).
3. Register the extractor class in `DependencyInjection.cs` under the pipeline. The pipeline automatically discovers and includes it in the sequential chain of responsibility.
