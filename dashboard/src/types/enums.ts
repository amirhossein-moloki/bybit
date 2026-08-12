export type OrderSide = "Buy" | "Sell";

export type OrderType = "Market" | "Limit";

export type OrderStatus =
  | "Created"
  | "Submitted"
  | "Accepted"
  | "PartiallyFilled"
  | "Filled"
  | "Cancelled"
  | "Rejected"
  | "Pending"
  | "New"
  | "Failed"
  | "ValidationFailed"
  | "ReadyForExchange"
  | "Unknown"
  | "Expired"
  | "Submitting";

export type PositionStatus =
  | "Pending"
  | "Open"
  | "PartiallyClosed"
  | "Closed"
  | "Liquidated";

export type CloseReason =
  | "StopLoss"
  | "TakeProfit"
  | "Manual"
  | "Signal"
  | "Liquidation"
  | "Exchange"
  | "Unknown";

export type HealthStatus = "Healthy" | "Degraded" | "Unhealthy" | "Unknown";

export type Severity = "CRITICAL" | "ERROR" | "WARNING" | "INFORMATION" | "INFO";

export type AggregationPeriod = "Daily" | "Weekly" | "Monthly";

export type WorkerState = "Running" | "Stopped" | "Started" | "Failed" | string;
