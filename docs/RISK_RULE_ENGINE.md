# Risk Rule Engine Architecture

This document details the architecture, design, execution flow, error handling policies, and extension guides for the **Risk Rule Engine** (Phase 05 — Stage 03) of the Telegram Signal Trading Bot.

---

## 1. Engine Core & Components

The **Risk Rule Engine** is implemented as part of the decoupled, Clean Architecture-compliant `TradingBot.Application` layer.

```
+------------------+      +------------------------+      +---------------------+
| TradeRiskContext | ---> |  RiskCalculationResult  | ---> |   IRiskRuleEngine   |
+------------------+      +------------------------+      +---------------------+
                                                                     |
                                                                     v
                                                          +---------------------+
                                                          |  RiskRuleExecutor   |
                                                          +---------------------+
                                                                     |
                                                +--------------------+--------------------+
                                                |                    |                    |
                                                v                    v                    v
                                            Rule 01              Rule 02               Rule 03
                                                |                    |                    |
                                                +--------------------+--------------------+
                                                                     |
                                                                     v
                                                          +---------------------+
                                                          |  RiskEvaluation     |
                                                          +---------------------+
```

### Key abstractions:
- **`IRiskRuleEngine`**: The main interface responsible for orchestrating the execution of rules, aggregating results, measuring execution times, and generating a persistent evaluation.
- **`RiskRuleEngine`**: The concrete implementation that resolves registered `IRiskRule` instances, validates the input context, invokes calculations, and aggregates outcomes.
- **`RiskRuleExecutor`**: A specialized helper class that handles safe, decoupled execution of individual rules. It intercepts unhandled exceptions, maintains diagnostic logging, and translates errors into critical rule failure responses.
- **`IRiskRule`**: The unified rule contract that each risk policy implements.

---

## 2. Rule Execution Order

All registered rules are executed **sequentially** in the order they are resolved from the Dependency Injection (DI) container. The standard order registered in `RiskManagementDependencyInjection` is:

1. `MaxRiskPerTradeRule`
2. `MaxOpenPositionsRule`
3. `MaximumLeverageRule`
4. `MaximumExposureRule`
5. `DailyLossRule`
6. `DrawdownRule`
7. `DuplicatePositionRule`
8. `RiskRewardRule`
9. `MarginAvailabilityRule`

---

## 3. Configuration & Options

The Risk Rule Engine is configured entirely via the standard `.NET Options Pattern` through the `RiskManagement` settings section. No values are hardcoded in the rules.

### Configuration Structure (`appsettings.json`):
```json
{
  "RiskManagement": {
    "Enabled": true,
    "DefaultProfile": "Balanced",
    "RejectOnCritical": true,
    "AutoReduceLeverage": false,
    "OnePositionPerSymbol": true,
    "MinimumRiskReward": 1.5,
    "MaximumExposure": 40.0,
    "MaximumDrawdown": 20.0,
    "MaximumDailyLoss": 5.0,
    "MaxRiskPerTrade": 1.0,
    "MaxOpenPositions": 5,
    "MaximumLeverage": 10
  }
}
```

- **`RejectOnCritical`**: If enabled, any rule failure with a `Critical` severity will immediately force the trade decision to `Rejected`.
- **`AutoReduceLeverage`**: If enabled, signal leverage exceeding `MaximumLeverage` is automatically reduced to the allowed limit rather than failing the check.

---

## 4. Severity & Decision Strategy

Each rule failure is categorized with a specific `RiskRuleSeverity`:

- **`Critical`**: Severe risk violations (e.g. Daily loss limit hit, Margin deficiency). Immediately rejects the trade (or flags for review depending on configuration).
- **`Error`**: Standard limit breaches (e.g. Risk per trade limit, duplicate symbol, max positions).
- **`Warning`**: Minor violations or automatic adjustments (e.g. Leverage automatically reduced).
- **`Info`**: Passed rules or informational checks.

---

## 5. Extension Guide (Adding New Rules)

To add a new risk rule to the bot:

1. **Create the Rule Class**:
   Create a class implementing `IRiskRule` in `TradingBot.Application/RiskManagement/Rules/`:
   ```csharp
   public class CustomRiskRule : IRiskRule
   {
       private readonly RiskManagementOptions _options;

       public CustomRiskRule(IOptions<RiskManagementOptions> options)
       {
           _options = options.Value;
       }

       public Task<RiskRuleResult> EvaluateAsync(TradeRiskContext context)
       {
           bool passed = true; // implement validation here
           return Task.FromResult(new RiskRuleResult
           {
               RuleName = nameof(CustomRiskRule),
               Passed = passed,
               Severity = RiskRuleSeverity.Error,
               Message = passed ? "Passed!" : "Failed!"
           });
       }
   }
   ```
2. **Add Properties to `RiskManagementOptions`**:
   If the rule requires new configurable thresholds, add them to `RiskManagementOptions.cs`.
3. **Register the Rule**:
   Register your new rule in `TradingBot.Infrastructure/RiskManagement/DependencyInjection.cs`:
   ```csharp
   services.AddScoped<IRiskRule, CustomRiskRule>();
   ```
4. **Write Tests**:
   Add test coverage for pass, fail, boundaries, and missing configurations under `tests/TradingBot.UnitTests/RiskManagement/RiskRulesTests.cs`.
