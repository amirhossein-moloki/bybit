# Telegram Signal Filtering & Detection Design

This document details the message filtering layer that identifies potential trading signal messages from incoming Telegram messages for the **Telegram Signal Trading Bot**.

---

## 1. Overview & Architecture

The message filtering layer acts as a gatekeeper in the Telegram ingest pipeline. It analyzes raw incoming Telegram messages and filters out general/chat messages, retaining only potential signal candidates for downstream parsing and execution.

### Processing Pipeline Flow
```
TelegramMessageDto
        ↓
Basic Validation (Null checks, empty text checks)
        ↓
Symbol Detection (BTC, ETH, etc. case-insensitive & alias mapped)
        ↓
Direction Detection (LONG, SHORT, BUY, SELL, 🟢, 🔴)
        ↓
Keyword Detection (Presence of entry/price or SL/TP risk keywords)
        ↓
Score Calculation (Total score computed based on matches)
        ↓
Threshold Filter (MinimumScore comparison)
        ↓
SignalCandidate (Filtered output)
```

### Components
- **`TelegramMessageDto`**: The raw data transfer object representing an incoming Telegram message.
- **`IMessageFilter`**: The abstraction interface defining the contract for analyzing incoming messages.
- **`MessageFilterService`**: The concrete implementation of `IMessageFilter` that implements the keyword extraction, matching, and scoring algorithms.
- **`SignalCandidate`**: The DTO output generated if a message meets or exceeds the required detection score.

---

## 2. Detection Rules & Keywords

To make rules highly extensible and support future localization (such as English, Persian, and custom user rules), keywords and aliases are fully configurable via the configuration settings block and structured across language files.

### 1. Symbol Detection
Supported base symbols and their mappings are configured dynamically:
- **Default Supported Symbols**: `BTCUSDT`, `ETHUSDT`, `SOLUSDT`, `XRPUSDT`, `BNBUSDT`
- **Default Symbol Aliases**:
  - `BTC` → `BTCUSDT`
  - `ETH` → `ETHUSDT`
  - `SOL` → `SOLUSDT`
  - `XRP` → `XRPUSDT`
  - `BNB` → `BNBUSDT`

*Match Strategy:* Symbols and aliases are sorted descending by length to match longer variants first (e.g., `BTCUSDT` before `BTC`), avoiding partial match confusion.

### 2. Direction Detection
Direction detection identifies long and short sentiment and keywords:
- **LONG (Bullish) Keywords**: `LONG`, `BUY`, `BULLISH`, `🟢`
- **SHORT (Bearish) Keywords**: `SHORT`, `SELL`, `BEARISH`, `🔴`

*Conflict Resolution:* If both LONG and SHORT keywords appear in a message, the side of the keyword that appears **earliest** in the message text is chosen as the dominant side.

### 3. Price Indicator Detection
Detects if the message contains price action related words without extracting values:
- **Price Keywords**: `ENTRY`, `BUY`, `SELL`, `PRICE`, `TARGET`

### 4. Risk Indicator Detection
Detects if risk parameters (Stop Loss and Take Profit) are present in the message:
- **Risk Keywords**: `SL`, `STOP LOSS`, `TP`, `TAKE PROFIT`

---

## 3. Scoring System

The bot implements a point-based heuristic scoring system to evaluate whether a message is a trade signal:

| Detection Metric | Score Weight | Description |
| :--- | :---: | :--- |
| **Symbol Found** | `+30` | Valid crypto symbol (or alias) detected in message text. |
| **Direction Found** | `+30` | LONG/SHORT direction keyword detected. |
| **Price Keyword Found** | `+20` | Presence of price/entry keyword detected. |
| **SL/TP Keyword Found** | `+20` | Presence of Stop Loss/Take Profit keyword detected. |
| **Maximum Score** | **`100`** | Highly complete trade signal candidate. |

### Signal Threshold
The minimum score threshold defaults to **`60`** (`MinimumScore` configuration setting).
- **Score ≥ 60**: The message is converted to a `SignalCandidate` and passed to the reliability layer.
- **Score < 60**: The message is ignored.

For example:
- `🚀 BTC LONG \n Entry 60000` → Symbol (30) + Direction (30) + Price Keyword (20) = **Score: 80** (SIGNAL CANDIDATE)
- `BTC LONG` → Symbol (30) + Direction (30) = **Score: 60** (SIGNAL CANDIDATE)
- `BTC Only` → Symbol (30) = **Score: 30** (IGNORED)

---

## 4. Multi-Language Support Foundation

The detection rules are organized inside an extensible model structure:
```
TradingBot.Application/Models/DetectionRules/
├── LanguageRules.cs (abstract base)
├── EnglishRules.cs
├── PersianRules.cs
└── CustomRules.cs
```
All active rules are automatically aggregated during validation. Adding support for a new language (e.g., Persian) is as simple as adding keywords to `PersianRules.cs`, with no changes needed in the core filter pipeline code.

---

## 5. Future Parser Integration

In subsequent phases, `SignalCandidate` outputs will be processed by a **Signal Parser**.
The parser will receive only messages identified as signal candidates. This ensures the complex NLP/regex-based price and parameter extractors do not waste resources processing standard group chats or general announcements, optimizing the bot's throughput and reliability.

---

## 6. Configuration Example

Add this section under your `appsettings.json`:
```json
{
  "SignalDetection": {
    "MinimumScore": 60
  }
}
```
All scoring keywords and symbols are read from default settings and can be fully overridden dynamically via the custom rules configurations.
