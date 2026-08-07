# Risk Rules Reference Catalog

This document serves as the comprehensive reference guide for all nine risk rules implemented in the **Risk Management Engine** of the Telegram Signal Trading Bot.

---

## 1. Max Risk Per Trade Rule

### Purpose
Ensures that the potential loss of a single trade (in the base currency / USDT) does not exceed a configured percentage of the total account equity.

### Configuration
- **Property**: `MaxRiskPerTrade`
- **Default Value**: `1.0%`

### Formula
$$\text{Max Allowed Loss} = \text{Account Balance} \times \frac{\text{MaxRiskPerTrade}}{100}$$
$$\text{Calculated Risk} = \text{RiskAmount (from RiskCalculationService)}$$

### Outcomes
- **Pass**: $\text{Calculated Risk} \leq \text{Max Allowed Loss}$
- **Fail**: $\text{Calculated Risk} > \text{Max Allowed Loss}$ (Severity: `Error`)

---

## 2. Max Open Positions Rule

### Purpose
Limits the total number of concurrent active positions on the trading account to prevent margin bloat and over-allocation.

### Configuration
- **Property**: `MaxOpenPositions`
- **Default Value**: `5`

### Outcomes
- **Pass**: $\text{Current Open Positions} < \text{MaxOpenPositions}$
- **Fail**: $\text{Current Open Positions} \geq \text{MaxOpenPositions}$ (Severity: `Error`)

---

## 3. Maximum Leverage Rule

### Purpose
Validates that the requested leverage of a trading signal is within safe leverage bounds.

### Configuration
- **Property**: `MaximumLeverage` (Default: `10`), `AutoReduceLeverage` (Default: `false`)

### Modes
- **Strict Mode** (`AutoReduceLeverage = false`): Rejects the trade if the signal leverage exceeds the maximum allowed leverage. (Severity: `Error`)
- **Auto-Reduce Mode** (`AutoReduceLeverage = true`): Automatically scales back the leverage to the configured limit, issues a `Warning` audit message, and allows execution to proceed. (Severity: `Warning`)

---

## 4. Maximum Exposure Rule

### Purpose
Ensures that the cumulative nominal exposure of all open positions plus the new trade doesn't exceed a safe percentage of the account balance.

### Configuration
- **Property**: `MaximumExposure`
- **Default Value**: `40.0%`

### Formula
$$\text{Max Exposure Limit} = \text{Account Balance} \times \frac{\text{MaximumExposure}}{100}$$
$$\text{New Nominal Size} = \text{PositionSize} \times \text{EntryPrice}$$
$$\text{Total Potential Exposure} = \text{CurrentExposure} + \text{New Nominal Size}$$

### Outcomes
- **Pass**: $\text{Total Potential Exposure} \leq \text{Max Exposure Limit}$
- **Fail**: $\text{Total Potential Exposure} > \text{Max Exposure Limit}$ (Severity: `Error`)

---

## 5. Daily Loss Protection Rule

### Purpose
A critical system-wide safety fuse that disables trading if the net daily PnL of the account drops below a negative threshold.

### Configuration
- **Property**: `MaximumDailyLoss`
- **Default Value**: `5.0%` (e.g. -500 USDT on a 10,000 USDT account)

### Formula
$$\text{Max Loss Threshold} = \text{Account Balance} \times \frac{\text{MaximumDailyLoss}}{100}$$

### Outcomes
- **Pass**: $\text{DailyPnL} > -\text{Max Loss Threshold}$
- **Fail**: $\text{DailyPnL} \leq -\text{Max Loss Threshold}$ (Severity: `Critical` - Disables Trading)

---

## 6. Drawdown Protection Rule

### Purpose
Stops execution of new trades if the account has suffered an excessive intraday peak-to-trough drop.

### Configuration
- **Property**: `MaximumDrawdown`
- **Default Value**: `20.0%`

### Formula
$$\text{Drawdown \%} = \begin{cases} \frac{-\text{DailyPnL}}{\text{Account Balance}} \times 100 & \text{if DailyPnL } < 0 \\ 0 & \text{otherwise} \end{cases}$$

### Outcomes
- **Pass**: $\text{Drawdown \%} \leq \text{MaximumDrawdown}$
- **Fail**: $\text{Drawdown \%} > \text{MaximumDrawdown}$ (Severity: `Critical`)

---

## 7. Duplicate Position Rule

### Purpose
Guarantees single exposure per symbol if enabled, preventing multiple concurrent trades on the same market.

### Configuration
- **Property**: `OnePositionPerSymbol`
- **Default Value**: `true`

### Outcomes
- **Pass**: No existing open position for the symbol exists in `IPositionRepository`.
- **Fail**: An open position for the symbol already exists in `IPositionRepository`. (Severity: `Error`)

---

## 8. Risk / Reward Rule

### Purpose
Filters out bad trade setups by requiring a minimum expected reward-to-risk ratio.

### Configuration
- **Property**: `MinimumRiskReward`
- **Default Value**: `1.5` (e.g. $1.5:1$)

### Outcomes
- **Pass**: $\text{Calculated Risk/Reward} \geq \text{MinimumRiskReward}$
- **Fail**: $\text{Calculated Risk/Reward} < \text{MinimumRiskReward}$ (Severity: `Error`)

---

## 9. Margin Availability Rule

### Purpose
Verifies that the account has sufficient available cash balance to post the margin required for the position.

### Configuration
- **Property**: None (relies on account state and calculations)

### Formula
$$\text{Available Cash} = \text{Account Balance} - \text{CurrentExposure}$$
$$\text{Required Position Margin} = \frac{\text{PositionSize} \times \text{EntryPrice}}{\text{Leverage}}$$

### Outcomes
- **Pass**: $\text{Required Position Margin} \leq \text{Available Cash}$
- **Fail**: $\text{Required Position Margin} > \text{Available Cash}$ (Severity: `Critical`)
