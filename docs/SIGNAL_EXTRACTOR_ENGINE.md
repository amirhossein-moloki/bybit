# Signal Extractor Engine - Phase 04 — Stage 02

This document details the architecture, design, and responsibilities of the **Signal Extractor Engine** implemented in **Phase 04 — Stage 02**.

---

## 1. Architectural Overview

The Signal Extractor Engine is responsible for extracting structured trading parameters from normalized, unstructured signal texts received from Telegram. The components are built strictly adhering to **Clean Architecture** and **Domain-Driven Design (DDD)**.

```
ParserContext
      ↓
[SignalTextNormalizer]
      ↓
SymbolExtractor ──────────> Extracts Crypto Symbol (e.g. BTCUSDT)
      ↓
DirectionExtractor ───────> Extracts Order Side (e.g. Long, Short)
      ↓
EntryExtractor ───────────> Extracts Entry Price (e.g. 60000)
      ↓
StopLossExtractor ────────> Extracts Stop Loss (e.g. 59000)
      ↓
TakeProfitExtractor ──────> Extracts Take Profits (e.g. [62000, 63000])
      ↓
LeverageExtractor ────────> Extracts Leverage (e.g. 20)
      ↓
ParsedSignal Model
```

---

## 2. Text Normalization (`SignalTextNormalizer`)

To make text extraction resilient to emojis, casing, and irregular spacings, raw inputs are run through `SignalTextNormalizer` before pattern matching.

### Responsibilities:
- **Emoji Removal**: Safe removal of emojis and other special symbols (using the Unicode categories `\p{Cs}|\p{Co}|\p{Cn}|\p{So}`).
- **Line Break Normalization**: Replaces carriage returns (`\r\n` and `\r`) with a standard newline character (`\n`).
- **Whitespace Collapsing**: Collapses multiple adjacent spaces and tabs into a single space, preserving single newlines.
- **Casing Normalization**: Converts all characters to standard uppercase for deterministic case-insensitive pattern matching.

**Example Transformation**:
- **Input**: `🔥 btc   long`
- **Output**: `BTC LONG`

---

## 3. Extractor Responsibilities & Supported Formats

Each extractor implements `ISignalExtractor` and performs specific pattern identification:

### 3.1. Symbol Extractor (`SymbolExtractor`)
- **Responsibility**: Identifies the base cryptocurrency coin or pair, and normalizes it to a `USDT` standard (e.g., Bybit contracts).
- **Supported Formats**:
  - Explicit pair formats: `BTCUSDT`, `ETH-USDT`, `SOL/USDT`, `XRP_USDC`
  - Bare coins: `BTC`, `ETH`, `SOL`
- **Output**: `"BTCUSDT"`

### 3.2. Direction Extractor (`DirectionExtractor`)
- **Responsibility**: Detects trading side and maps to `OrderSide.Buy` or `OrderSide.Sell`.
- **Supported Long Keywords**: `LONG`, `BUY`, `LONG POSITION`, `BULLISH`
- **Supported Short Keywords**: `SHORT`, `SELL`, `SHORT POSITION`, `BEARISH`
- **Output**: `OrderSide.Buy` or `OrderSide.Sell`

### 3.3. Entry Price Extractor (`EntryExtractor`)
- **Responsibility**: Extracts the first valid entry target price.
- **Supported Formats**:
  - Direct values: `Entry: 60000`
  - Range zones: `Buy Zone: 60000-60500` (extracts `60000`)
  - Market entries: `Entry Now` (no price extracted, no format error added)
- **Output**: `60000` (decimal)

### 3.4. Stop Loss Extractor (`StopLossExtractor`)
- **Responsibility**: Extracts the protective stop loss price.
- **Supported Formats**:
  - Direct values: `SL: 59000` or `Stop Loss: 59000`
- **Output**: `59000` (decimal)

### 3.5. Take Profit Extractor (`TakeProfitExtractor`)
- **Responsibility**: Identifies multiple target prices, removes duplicates, and preserves order.
- **Supported Formats**:
  - Multiple target definitions: `TP1:62000`, `TP2:63000`, `Target:65000`
- **Output**: `[62000, 63000, 65000]` (`List<decimal>`)

### 3.6. Leverage Extractor (`LeverageExtractor`)
- **Responsibility**: Identifies maximum target leverage multiplier.
- **Supported Formats**:
  - Suffix format: `10x`, `20X`
  - Explicit label format: `Leverage: 50`
- **Output**: `10` (integer)

---

## 4. Error Handling & Fault Isolation

- **Extractor Independence**: To prevent a single format change or unexpected message from crashing the system, each extractor runs independently in a safe `try-catch` wrapper inside the pipeline.
- **Error Propagation**: Any parsing or formatting exceptions are compiled into `ParsedSignal.Errors` and returned in the final failed `ParserResult`.
- **Missing Data Warnings**: If optional data is missing, the system does not fail, but compiles diagnostics into `ParserResult.Warnings` (e.g. `"Entry not detected"`, `"Stop loss not detected"`, etc.).

---

## 5. Future Extension Points & Rules Engine (`IExtractionRule`)

The architecture defines `IExtractionRule` to support pluggable rules:
```csharp
public interface IExtractionRule
{
    bool Match(string text);
    object Extract(string text);
}
```

This interface facilitates:
- **Pluggable Regex Rules**: Channel-specific keyword maps or custom layout rules.
- **AI-Assisted Rules**: Integration of Large Language Model (LLM) extractors for freeform text structures.
- **Fallback / Validation Chains**: Sequential try-match fallback chains per channel style.
