# Risk Management Engine Foundation Architecture

This document details the foundation layer, core architectural design, domain models, interfaces, and future extensibility roadmap for the Risk Management Engine of the Telegram Signal Trading Bot.

---

## 1. Overview & Purpose

The **Risk Management Engine (RME)** is a critical subsystem positioned between the parsed, validated trade signals and the trading execution pipeline. Its primary mission is to:
1. Protect trading capital through strict risk validation policies.
2. Calculate correct trade sizes and exposure amounts dynamically.
3. Make robust trade decisions (Approve, Reject, or Hold for manual review) based on composite risk rules.

```
+------------------+      +---------------------+      +----------------------+      +------------------+
| Validated Signal | ---> | Risk Engine Service | ---> | Composite Risk Rules | ---> |  Trade Decision  |
+------------------+      +---------------------+      +----------------------+      +------------------+
                                     |
                                     v
                          +---------------------+
                          | Position Sizer      |
                          +---------------------+
```

---

## 2. Core Domain Models

The domain models represent the mathematical context and entities used to validate risk.

### RiskProfile
An entity representing user or system-wide risk limits.
- `Id`: Unique profile identifier.
- `Name`: Profile name (e.g., "Conservative", "Balanced", "Aggressive").
- `MaxRiskPerTrade`: Maximum risk percentage allowed per individual trade.
- `MaxDailyLoss`: Maximum daily drawdown allowed before trading is paused.
- `MaxWeeklyLoss`: Maximum weekly drawdown.
- `MaxMonthlyLoss`: Maximum monthly drawdown.
- `MaxOpenPositions`: Maximum concurrent open positions.
- `MaxLeverage`: Maximum leverage factor allowed.
- `MaxExposure`: Maximum total exposure.
- `MinimumRiskReward`: Minimum risk-to-reward ratio for trade entry.
- `CreatedAt` & `UpdatedAt`: Standard audit timestamps.

### TradeRiskContext
An immutable value object record containing all context data required for evaluation.
- `SignalId`: The source signal identifier.
- `Symbol`: Target trading pair symbol.
- `Side`: Buy or Sell direction.
- `EntryPrice`: Target entry price.
- `StopLoss`: Associated stop loss price.
- `TakeProfits`: Collection of take profit targets.
- `Leverage`: Optional target leverage.
- `AccountBalance`: Current trading account balance.
- `OpenPositions`: Number of currently open positions.
- `DailyPnL`: Current daily profits or losses.
- `CurrentExposure`: Current total margin or asset exposure.

### RiskEvaluation
An entity representing the persistent audit trail of a risk execution run.
- `Id`: Evaluation execution ID.
- `SignalId`: Source signal ID.
- `RiskAmount`: Calculated money amount at risk.
- `PositionSize`: Suggested optimal position size.
- `RiskReward`: Calculated risk-to-reward ratio.
- `Exposure`: Calculated margin/capital exposure.
- `Decision`: Resulting status of the decision.
- `Reason`: Summary of the reasoning behind the decision.

### TradeDecision
The final decision record returned by the Risk Engine.
- `Decision`: One of `Approved`, `Rejected`, or `NeedsReview`.
- `Approved`: Boolean helper flag indicating whether the trade is ready for immediate execution.
- `Rejected`: Boolean helper flag indicating trade rejection.
- `NeedsReview`: Boolean helper flag indicating trade should be held for human operators to inspect.
- `Reason`: Compiled reason string.

---

## 3. Interfaces & Abstractions

Our architecture relies entirely on decoupled abstractions, making it simple to plug in new behaviors without modifying existing code.

### IRiskEngine
```csharp
public interface IRiskEngine
{
    Task<TradeDecision> EvaluateAsync(TradeRiskContext context);
}
```

### IRiskRule
The base contract that all future risk validation rules will implement.
```csharp
public interface IRiskRule
{
    Task<RiskRuleResult> EvaluateAsync(TradeRiskContext context);
}
```

### IPositionSizeCalculator
Decouples position sizing formulas from the main risk engine.
```csharp
public interface IPositionSizeCalculator
{
    decimal Calculate(TradeRiskContext context);
}
```

### IRiskDecisionService
Compiles a set of rule evaluation results into a single final decision based on failure severity.
```csharp
public interface IRiskDecisionService
{
    TradeDecision CreateDecision(IEnumerable<RiskRuleResult> results);
}
```

---

## 4. Error & Exception Handling

We introduce `RiskManagementException` under `TradingBot.Application.RiskManagement.Exceptions` inheriting from `TradingBot.Application.Exceptions.ApplicationException`.
It cleanly covers domain and infrastructure errors such as:
- Invalid or missing risk profiles.
- Absent or corrupt account data.
- Invalid configuration parameters.

---

## 5. Security & Sensitive Logging Compliance

The Risk Engine Foundation implements strict logging standards. It tracks execution state thread-safely while strictly avoiding leakage of any credentials, API keys, secrets, or individual account identities.

**Allowed logs:**
- `Risk Evaluation Started`
- `Risk Engine Initialized`
- `Risk Configuration Loaded`

**Forbidden logs:**
- API Keys, private keys, secrets, or session tokens.

---

## 6. Future Extensions & Stage 02 Path

The foundation is built to easily scale. To implement future stages (e.g., leverage validation, position sizing, exposure caps):
1. **Create Rules**: Implement `IRiskRule` (e.g., `MaxLeverageRule`, `DailyLossRule`, `ExposureRule`).
2. **Register Rules**: Register the rules in Dependency Injection using standard transient or scoped lifetimes. The `RiskEngineService` automatically resolves all registered rules and executes them sequentially.
3. **Sizer Engine**: Implement the `IPositionSizeCalculator` (e.g., using Kelly Criterion or Percent-of-Equity sizers) and register it in DI.
