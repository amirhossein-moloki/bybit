# Phase 06 — Stage 02: Order Builder & Exchange Order Validation

This document details the design, architecture, and implementation of Stage 02 — Order Builder & Exchange Order Validation under the Trading Execution Engine (Phase 06).

---

## 1. Objectives & Scope

Extend the execution foundation established in Stage 01 to support converting approved trades into fully normalized and validated internal order requests, ready to be handed off to the Bybit Exchange Adapter (Stage 03).

### Key Features:
- **Order Construction**: Transform `TradeExecutionRequest` into `OrderRequest` using the Order Builder.
- **Symbol Normalization**: Convert various external representations (e.g., `btc/usdt`, `BTC-USDT`, `btc usdt`) into a canonical representation (`BTCUSDT`) before further processing.
- **Deterministic Validation Pipeline**: Enforce strict validation steps including:
  1. Risk approval check
  2. Structural/domain checks (Symbol, side, order type, quantity, limit price)
  3. Instrument Constraint checks (Tick size, quantity step, minimum quantity, maximum quantity, minimum notional)
- **Structured Results & Codes**: Provide rich feedback via `OrderValidationResult` carrying explicit, machine-readable validation codes.
- **Fail-Closed Behavior**: Automatically block execution and reject trade dispatches if essential safety parameters (like risk approval or exchange instrument rules) are missing.

---

## 2. Architecture & Execution Flow

```text
Approved Trade Decision
        ↓
TradeExecutionRequest
        ↓
OrderBuilder.Build() (Symbol normalized to canonical uppercase)
        ↓
OrderRequest
        ↓
OrderValidator.Validate() (Verifies against IExchangeInstrumentRules)
        ↓
OrderValidationResult (Check .IsValid)
        ↓
  [If Valid] ──> Status: ReadyForExchange (Gateway call bypassed in Stage 02)
  [If Invalid] ──> Status: ValidationFailed (Blocked; 0 gateway calls)
```

---

## 3. Validation Pipeline & Machine-Readable Codes

The validation pipeline enforces rules sequentially without dependency order failures, generating structured errors or warnings.

### Implemented Validation Codes

| Code | Severity | Description |
|---|---|---|
| `RISK_APPROVAL_REQUIRED` | Critical | Risk decision is not `Approved` (e.g. `Rejected`, `NeedsManualReview`, `NeedsReview`). |
| `INVALID_SYMBOL` | Critical | Symbol is empty, whitespace, or less than 3 characters long. |
| `INVALID_SIDE` | Critical | Order side is neither `Buy` nor `Sell`. |
| `INVALID_ORDER_TYPE` | Critical | Order type is neither `Market` nor `Limit`. |
| `INVALID_QUANTITY` | Critical | Requested quantity is less than or equal to zero. |
| `INVALID_LIMIT_PRICE` | Critical | For Limit orders, the requested limit price is less than or equal to zero. |
| `MISSING_INSTRUMENT_RULES` | Critical | Fail-closed protection triggers if instrument rules cannot be found for the specified symbol. |
| `QUANTITY_BELOW_MINIMUM` | Error | Requested quantity is below `MinQuantity` configured for the instrument. |
| `QUANTITY_ABOVE_MAXIMUM` | Error | Requested quantity is above `MaxQuantity` configured for the instrument. |
| `INVALID_QUANTITY_STEP` | Error | Requested quantity does not align with the instrument's `QuantityStep` constraint. |
| `INVALID_PRICE_TICK` | Error | For Limit orders, the requested price does not align with the instrument's `TickSize`. |
| `PRICE_BELOW_MINIMUM` | Error | For Limit orders, the requested price is below the minimum tick size allowed (`Price < TickSize`). |
| `NOTIONAL_BELOW_MINIMUM` | Error | Order value (Quantity × Price) is below the exchange's minimum notional value (`MinNotional`). |

---

## 4. Key Implementation Details

### 4.1 Symbol Normalization
To prevent duplicate mapping logic, `SymbolNormalizer` cleans up the symbol format in an exchange-independent manner:
```csharp
public static string Normalize(string symbol)
{
    if (string.IsNullOrWhiteSpace(symbol)) return string.Empty;
    return symbol.Replace("/", "").Replace("-", "").Replace(" ", "").Trim().ToUpperInvariant();
}
```

### 4.2 Precision-Safe Checking
Checks for tick size and quantity step use decimal-safe remainder arithmetic with a standard precision tolerance (`1e-10m`) to prevent floating-point anomalies:
```csharp
decimal quantityRemainder = orderRequest.Quantity % instrumentRules.QuantityStep;
decimal tolerance = 1e-10m;
if (quantityRemainder > tolerance && (instrumentRules.QuantityStep - quantityRemainder) > tolerance)
{
    result.AddError($"Requested quantity {orderRequest.Quantity} does not satisfy QuantityStep of {instrumentRules.QuantityStep}.", "INVALID_QUANTITY_STEP");
}
```

### 4.3 Fail-Closed Protection
If `InstrumentRules` is null, the validation immediately reports `MISSING_INSTRUMENT_RULES` with `Critical` severity, preventing execution and completely avoiding hardcoded/assumed defaults.

---

## 5. Gateway Call Bypassing in Stage 02

During Stage 02, no real orders are sent to Bybit, and no mock gateway transitions are faked.
- For a valid order request: returns an `ExecutionResult` with `Status = OrderStatus.ReadyForExchange` and `Success = true`.
- For an invalid order request: returns an `ExecutionResult` with `Status = OrderStatus.ValidationFailed` and `Success = false`.

The `TestExchangeTradingGateway` remains present in the system, but is asserted to have an invocation count of `0` during execution validations.

---

## 6. How to Run Tests

Run the newly developed unit and integration test suites:
```bash
dotnet test --filter "FullyQualifiedName~TradingExecution"
```
Or execute the entire regression suite to verify complete codebase health:
```bash
dotnet test
```
