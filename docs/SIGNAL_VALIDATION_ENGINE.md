# Signal Validation Engine Architecture

The **Signal Validation Engine** resides in the `TradingBot.Parser` module. It forms the second stage of the Signal Processing pipeline. Its primary responsibility is to accept a parsed signal and run a suite of configurable validation rules to determine whether the signal meets all format, exchange support, and logical business constraints required before it can be passed to the Risk Management Engine.

---

## 1. Validation Architecture

The engine is built around a highly modular, decoupled design adhering to SOLID principles. The files are organized as follows:

```text
TradingBot.Parser
├── Validation
│   ├── ISignalValidator.cs
│   ├── IValidationRule.cs
│   ├── ValidationEngine.cs
│   ├── ValidationContext.cs
│   ├── ValidationResult.cs
│   └── Rules
│       ├── SymbolValidationRule.cs
│       ├── DirectionValidationRule.cs
│       ├── EntryValidationRule.cs
│       ├── StopLossValidationRule.cs
│       ├── TakeProfitValidationRule.cs
│       ├── LeverageValidationRule.cs
│       └── BusinessConsistencyValidationRule.cs
```

Each validation rule is highly isolated, implements the `IValidationRule` interface, and is registered in the DI container. The `ValidationEngine` aggregates these rules and executes them independently.

---

## 2. Abstractions & Core Models

### `IValidationRule`
Every validation rule implements this interface, defining a single task:
```csharp
public interface IValidationRule
{
    Task ValidateAsync(ValidationContext context, ValidationResult result);
}
```

### `ValidationContext`
Provides immutable execution context containing the parsed signal data, raw signal ID, source channel, template metadata, and validation start time:
- `SignalId`
- `ParsedSignal`
- `SourceChannel`
- `TemplateName`
- `ParserVersion`
- `ValidationStartedAt`

### `ValidationResult`
Tracks the final validation state, including:
- `IsValid` (bool)
- `ValidationStatus` (enum string: `Validated`, `Rejected`, `RequiresReview`)
- `Errors` (list of strings explaining why validation failed)
- `Warnings` (list of non-blocking warnings)
- `FailedRules` (list of rule names that failed)
- `ValidatedAt` (DateTime)

---

## 3. Implemented Validation Rules

### 1. Symbol Validation (`SymbolValidationRule`)
- **Purpose:** Verifies that a symbol was extracted, conforms to expected format regulations (uppercase, alphanumeric, 3 to 20 characters), and exists as a supported asset in the active trading configuration.
- **Action:** Rejects if empty, malformed, or if `RejectUnknownSymbols` is active and the symbol is not found in the DB `Symbols` repository.

### 2. Direction Validation (`DirectionValidationRule`)
- **Purpose:** Verifies the signal side/direction.
- **Action:** Allowed sides are `LONG` and `SHORT` (mapped to `OrderSide.Buy` and `OrderSide.Sell`). Rejects if missing or invalid.

### 3. Entry Price Validation (`EntryValidationRule`)
- **Purpose:** Checks the entry price.
- **Action:** Must be numeric, present, and strictly positive (> 0).

### 4. Stop Loss Validation (`StopLossValidationRule`)
- **Purpose:** Validates the stop loss.
- **Action:** If `RequireStopLoss` is active, it must exist. If it exists, it must be strictly positive.

### 5. Take Profit Validation (`TakeProfitValidationRule`)
- **Purpose:** Validates take profit targets.
- **Action:** If `RequireTakeProfit` is active, at least one target must exist. All existing targets must be strictly positive.

### 6. Leverage Validation (`LeverageValidationRule`)
- **Purpose:** Validates leverage settings.
- **Action:** If present, leverage must be strictly positive and fall within the `MaximumLeverage` option. If omitted, the rule does not reject.

### 7. Business Consistency Validation (`BusinessConsistencyValidationRule`)
- **Purpose:** Validates the math/logic of the trade parameters based on the direction.
- **Action:**
  - **For LONG:** `StopLoss < EntryPrice` and all `TakeProfit targets > EntryPrice`.
  - **For SHORT:** `StopLoss > EntryPrice` and all `TakeProfit targets < EntryPrice`.

---

## 4. Configuration Options

The validation parameters can be customized via the `"Validation"` section in `appsettings.json`:

```json
{
  "Validation": {
    "RequireStopLoss": true,
    "RequireTakeProfit": true,
    "MaximumLeverage": 100,
    "RejectUnknownSymbols": true
  }
}
```

These parameters are bound into a strongly-typed `ValidationOptions` class injected via `IOptions<ValidationOptions>`.
