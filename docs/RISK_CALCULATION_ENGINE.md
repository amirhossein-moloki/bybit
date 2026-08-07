# Position Size Calculator & Risk Calculation Engine

## Overview

The financial calculation layer of the Risk Management Engine is responsible for executing highly precise and mathematically sound risk calculations before a trade is approved. This engine is structured under Clean Architecture, with the calculators operating within `TradingBot.Application` utilizing the highly precise C# `decimal` type to prevent binary floating-point rounding errors.

---

## 1. Core Mathematical Formulas

### A. Risk Amount
Calculates the maximum amount of capital (in USDT or quote asset) allowed to be lost on a single trade.
$$\text{Risk Amount} = \text{Account Balance} \times \frac{\text{Risk Percentage}}{100}$$

*Example:*
- Balance: `1000 USDT`
- Risk: `1%`
- Result: `10 USDT`

---

### B. Stop Loss Distance
Calculates the absolute price difference between the Entry Price and the Stop Loss Price based on the direction (LONG or SHORT) of the order.

- **LONG (Buy Order):**
$$\text{Stop Loss Distance} = \text{Entry Price} - \text{Stop Loss Price}$$

- **SHORT (Sell Order):**
$$\text{Stop Loss Distance} = \text{Stop Loss Price} - \text{Entry Price}$$

*Example (LONG):*
- Entry Price: `60,000`
- Stop Loss: `59,000`
- Result: `1,000`

---

### C. Position Size
Calculates the total unit/asset quantity of the trade based on the maximum allowed risk amount and the stop loss distance.
$$\text{Position Size} = \frac{\text{Risk Amount}}{\text{Stop Loss Distance}}$$

*Example:*
- Risk Amount: `10 USDT`
- Stop Loss Distance: `1,000`
- Result: `0.01 BTC`

---

### D. Risk / Reward Ratio (R:R)
Measures the relationship between potential reward and potential risk of the trade. Supports single, first, and average Take Profits (TP).
$$\text{Risk Reward} = \frac{\text{Reward Distance}}{\text{Risk Distance}}$$

- **LONG (Buy Order):**
$$\text{Risk Distance} = \text{Entry Price} - \text{Stop Loss Price}$$
$$\text{Reward Distance} = \text{Take Profit Price} - \text{Entry Price}$$

- **SHORT (Sell Order):**
$$\text{Risk Distance} = \text{Stop Loss Price} - \text{Entry Price}$$
$$\text{Reward Distance} = \text{Entry Price} - \text{Take Profit Price}$$

---

### E. Required Margin
Calculates the actual collateral needed to open the position based on the calculated position size and leverage.
$$\text{Required Margin} = \frac{\text{Position Size} \times \text{Entry Price}}{\text{Leverage}}$$

---

## 2. Precision & Rounding Rules

1. **Floating Point Prevention:** Under no circumstances are `double` or `float` types used inside the calculation engine. All financial quantities, prices, distances, and margins are calculated using high-precision `decimal`.
2. **Rounding Precision:** All calculated values are rounded to the configurable `RoundingPrecision` parameter (default `8` decimal places) using `MidpointRounding.AwayFromZero` to match the precision characteristics of major crypto exchanges (e.g., Bybit, Binance).

---

## 3. Error & Validation Handling

| Validation Scenario | Trigger Condition | System Result | Exception Message / Log |
| :--- | :--- | :--- | :--- |
| **Missing Balance** | `AccountBalance <= 0` | Calculation Failed / Rejected | `Calculation Failed: Missing or invalid account balance.` |
| **Missing Stop Loss** | `StopLoss == null` | Cannot Calculate Risk | `Cannot Calculate Risk: Missing stop loss.` |
| **Zero Distance** | `EntryPrice == StopLoss` | Reject Calculation | `Reject Calculation: Stop loss distance is zero.` |
| **Negative Distance** | `EntryPrice - StopLoss < 0` (LONG) or `StopLoss - EntryPrice < 0` (SHORT) | Reject Calculation | `Reject Calculation: Stop loss distance is negative.` |
| **Invalid Risk %** | `DefaultRiskPercent < 0` | Invalid Configuration | `Invalid Configuration: Invalid risk percentage.` |

These exceptions are gracefully handled by `RiskEngineService` which converts them into a `Rejected` `TradeDecision` and stores the corresponding failure reason in the `RiskEvaluations` database table.

---

## 4. DB Audit Persistence

Every risk evaluation is recorded in the `RiskEvaluations` table via Entity Framework Core. Each audit record includes:
- `SignalId` (links to the corresponding Trade Signal)
- `RiskAmount`
- `PositionSize`
- `RiskReward`
- `Exposure` (calculated as `CurrentExposure + PositionSize * EntryPrice`)
- `Decision` (`Approved`, `Rejected`, or `NeedsReview`)
- `Reason`
- `CreatedAt` (UTC Timestamp)
