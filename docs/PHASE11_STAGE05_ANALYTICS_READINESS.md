# PHASE 11 — STAGE 05: Analytics Validation, Integration & Production Readiness

This document outlines the architecture, API contracts, metrics definition, and data rules of the complete, validated read-only Trading Analytics system.

---

## 1. System Overview & Isolation

The Trading Analytics system is entirely **isolated** from all write-heavy and timing-critical Trading execution modules. It operates purely as a **read-only** projection model querying finalizedcompleted trades, historical equity transitions, and reporting schedules.

All database queries utilize high-performance projections and non-tracking (`AsNoTracking()`) Entity Framework Core queries. To prevent database locks and latency overhead, large exports are streamed on-the-fly using `IAsyncEnumerable`.

---

## 2. Database Index & Query Optimization

To handle high volumes of trades efficiently, database indexes are defined on query filter properties:
- **Trades Table**:
  - `ClosedAt` (index): Fast chronological sorting and date filtering.
  - `Symbol` (index): Accelerates symbol-specific analytics filtering.
  - `Side` (index): Accelerates side/direction-specific performance filtering.
  - `PositionId` (index): Joins trades back to their parent positions.
- **Signals Table**:
  - `CreatedAt` (index): Fast chronological filtering of signal detection logs.
  - `Status` (index): Filters parsed vs pending signal records.

---

## 3. Available API Endpoints

All endpoints are mapped under the `/api/analytics` path and secured via standard ASP.NET Core Bearer Token Authentication, requiring the `DashboardRead` policy (asserting `Permission = "dashboard.read"`).

### 3.1. Overview Analytics
- **Endpoint**: `GET /api/analytics/overview`
- **Purpose**: Retrieves a comprehensive summary of all trading statistics.
- **Query Parameters**:
  - `startDate` / `from` (string, optional): ISO-8601 start date-time filter.
  - `endDate` / `to` (string, optional): ISO-8601 end date-time filter.
  - `symbol` (string, optional): Trading pair (e.g., `BTCUSDT`).
  - `side` (string, optional): Execution side (`Buy` or `Sell`).
- **Response**:
  ```json
  {
    "status": "success",
    "data": {
      "totalTrades": 3,
      "winningTrades": 2,
      "losingTrades": 1,
      "breakevenTrades": 0,
      "winRate": 66.67,
      "lossRate": 33.33,
      "grossProfit": 2500.0,
      "grossLoss": 500.0,
      "netPnL": 2000.0,
      "averagePnL": 666.67,
      "averageWin": 1250.0,
      "averageLoss": 500.0,
      "largestWin": 1500.0,
      "largestLoss": 500.0,
      "profitFactor": 5.0,
      "averageDuration": "01:30:00",
      "shortestDuration": "00:45:00",
      "longestDuration": "02:15:00",
      "currentWinStreak": 1,
      "currentLossStreak": 0,
      "maximumWinStreak": 2,
      "maximumLossStreak": 1
    }
  }
  ```

### 3.2. PnL Analytics Summary
- **Endpoint**: `GET /api/analytics/pnl`
- **Purpose**: Focused lightweight summary of financial results.
- **Query Parameters**: Same as `/overview`.
- **Response**:
  ```json
  {
    "status": "success",
    "data": {
      "grossProfit": 2500.0,
      "grossLoss": 500.0,
      "netPnL": 2000.0,
      "averagePnL": 666.67,
      "profitFactor": 5.0
    }
  }
  ```

### 3.3. Symbol Performance
- **Endpoint**: `GET /api/analytics/symbols`
- **Purpose**: Groups and analyzes trading statistics aggregated per symbol.
- **Query Parameters**:
  - `startDate` / `from` (string, optional).
  - `endDate` / `to` (string, optional).
- **Response**:
  ```json
  {
    "status": "success",
    "data": [
      {
        "symbol": "BTCUSDT",
        "totalTrades": 2,
        "winningTrades": 2,
        "losingTrades": 0,
        "winRate": 100.0,
        "netPnL": 1990.0,
        "grossProfit": 1990.0,
        "grossLoss": 0.0,
        "averagePnL": 995.0
      }
    ]
  }
  ```

### 3.4. Signal / Directional Performance
- **Endpoint**: `GET /api/analytics/signals`
- **Purpose**: Groups and analyzes trading statistics per signal side (Buy/Long vs Sell/Short).
- **Query Parameters**:
  - `startDate` / `from` (string, optional).
  - `endDate` / `to` (string, optional).
  - `symbol` (string, optional).
- **Response**:
  ```json
  {
    "status": "success",
    "data": [
      {
        "side": "Buy",
        "totalTrades": 5,
        "winningTrades": 3,
        "losingTrades": 2,
        "winRate": 60.0,
        "netPnL": 1400.0,
        "grossProfit": 2000.0,
        "grossLoss": 600.0,
        "averagePnL": 280.0
      }
    ]
  }
  ```

### 3.5. Equity Curve
- **Endpoint**: `GET /api/analytics/equity` (aliased to `/equity-curve`)
- **Purpose**: Compiles a trade-by-trade coordinate stream of historical equity and drawdowns.
- **Query Parameters**:
  - `startDate` / `from`, `endDate` / `to`, `symbol`, `side`.
  - `initialBalance` (decimal, optional, default: 10,000): Starting capital.
- **Response**:
  ```json
  {
    "status": "success",
    "data": [
      {
        "tradeIndex": 1,
        "tradeId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
        "closedAt": "2023-11-01T12:00:00Z",
        "netPnL": 990.0,
        "cumulativePnL": 990.0,
        "equity": 10990.0,
        "drawdown": 0.0,
        "drawdownPercentage": 0.0,
        "peakEquity": 10990.0
      }
    ]
  }
  ```

### 3.6. Full Performance Report
- **Endpoint**: `GET /api/analytics/report`
- **Purpose**: Compiles a comprehensive analytical report containing overall metrics, streaks, durations, and detailed trade histories.
- **Query Parameters**: Standard filters plus `minPnL`, `maxPnL`, `closeReason`, `initialBalance`, and `bypassCache`.
- **Response**: Detailed multi-section performance report JSON.

### 3.7. CSV Export
- **Endpoint**: `GET /api/analytics/export/csv`
- **Purpose**: Streams all matching completed trades as a CSV spreadsheet.
- **Content-Type**: `text/csv`

---

## 4. Metrics Definition

### 4.1. Win Rate
Defined as the percentage of completed trades that ended with a positive Net PnL.
$$\text{Win Rate} = \left( \frac{\text{Winning Trades}}{\text{Total Completed Trades}} \right) \times 100$$

### 4.2. Profit Factor
Defined as the ratio of gross profits to gross losses. It represents how many units of currency are made for each unit of currency lost.
$$\text{Profit Factor} = \frac{\sum \text{Positive NetPnL}}{\sum | \text{Negative NetPnL} |}$$
*Note: If gross loss is $0$, Profit Factor is safely returned as $0$.*

### 4.3. Drawdown (Peak-to-Trough)
Calculated continuously trade-by-trade:
- $\text{Peak Equity} = \max(\text{Previous Equity}, \text{Current Equity})$
- $\text{Drawdown} = \text{Peak Equity} - \text{Current Equity}$
- $\text{Drawdown \%} = \left( \frac{\text{Drawdown}}{\text{Peak Equity}} \right) \times 100$

### 4.4. Streaks
- **Current Win Streak**: Consecutive trades ending with a positive Net PnL.
- **Current Loss Streak**: Consecutive trades ending with a negative Net PnL.
- *Note: Breakeven trades ($\text{Net PnL} = 0$) immediately reset both win and loss streaks.*

---

## 5. System Constraints & Data Rules

- **UTC Timezone Only**: All timestamps returned in APIs or parsed from queries must represent exact UTC date-times. Non-UTC inputs are automatically normalized to UTC before execution.
- **Inclusion Criteria**: Only finalized completed trades (where both `ClosedAt` and `PositionId` are non-null) participate in analytics. Underway positions or pending fills are entirely excluded to prevent dirty read projections.
- **Mathematical Precision**: All pricing, fees, PnLs, and equity values are calculated and stored using high-precision 128-bit `decimal` types. Rounding precision is dynamically configured per exchange rules.
- **Sensitive Fields Redaction**: To preserve production security, all analytics and export results are verified and audited to ensure no sensitive fields (e.g., API keys, secrets, Telegram tokens, or internal passwords) can ever leak in payloads.
