# Bybit Execution Adapter Architecture & Testnet Order Submission

This document outlines the design, architecture, integration, security rules, and testing strategies for the **Bybit Execution Adapter (Phase 06 — Stage 03)**.

---

## 1. Architecture Overview

The Trading Execution Engine workflow coordinates order creation, validation, signing, transmission, response parsing, and error translation:

```text
Validated Signal
        ↓
Risk Management
        ↓
Approved Trade
        ↓
TradeExecutionRequest
        ↓
TradingExecutionService
        ↓
IExchangeTradingGateway (BybitExecutionAdapter)
        ↓
Bybit Unified V5 API
        ↓
Exchange Order ID
        ↓
ExecutionResult
```

### Clean Architecture Isolation
All Bybit-specific models, DTOs, endpoint routes, HMAC signing algorithms, and HTTP request headers are isolated within the `TradingBot.Exchange.Bybit` project. The Application layer remains completely exchange-agnostic, interacting solely via `IExchangeTradingGateway`, `OrderRequest`, and `OrderResult`.

---

## 2. Dynamic Testnet & Production Resolution

To guarantee security and eliminate accidental mainnet execution, the configuration separates environments clearly and enforces **Testnet** as the default.

### Config Options
```json
{
  "Bybit": {
    "Environment": "Testnet",
    "ApiKey": "YOUR_API_KEY",
    "ApiSecret": "YOUR_API_SECRET",
    "RecvWindow": 5000
  }
}
```

- If `Environment` is set explicitly to `"Production"` (case-insensitive), the adapter resolves the API endpoint to:
  `https://api.bybit.com` (Bybit Mainnet API)
- For any other values (including `"Testnet"`, empty, or missing), it defaults to:
  `https://api-testnet.bybit.com` (Bybit Testnet API)

---

## 3. Unified V5 API Request Mapping

All orders in this adapter target the `category: linear` (USDT Perpetual Futures).

### Create Order Payload Map
- **Market Order Payload:**
  ```json
  {
    "category": "linear",
    "symbol": "BTCUSDT",
    "side": "Buy",
    "orderType": "Market",
    "qty": "0.01",
    "orderLinkId": "BOT-170000000000"
  }
  ```
- **Limit Order Payload:**
  ```json
  {
    "category": "linear",
    "symbol": "BTCUSDT",
    "side": "Buy",
    "orderType": "Limit",
    "qty": "0.01",
    "price": "60000",
    "orderLinkId": "BOT-170000000000"
  }
  ```

---

## 4. Authentication & HMAC Signing

Authentication uses standard Bybit Unified V5 API signature mechanisms.
`signature = HMAC-SHA256(apiSecret, timestamp + apiKey + recvWindow + payload)`

### Request Headers
- `X-BAPI-API-KEY`: `<ApiKey>`
- `X-BAPI-SIGN`: `<Computed Signature>`
- `X-BAPI-SIGN-TYPE`: `2`
- `X-BAPI-TIMESTAMP`: `<Current Unix Millisecond Timestamp>`
- `X-BAPI-RECV-WINDOW`: `<RecvWindow>`

### Safe Redacted Logging
To ensure zero exposure of private keys or credentials:
- **API Secrets, computed signatures, and authentication headers are never logged.**
- Requests log only safe fields: `Symbol`, `OrderType`, `Side`, `Quantity`, `ExchangeOrderId`, `Status`, and network latency.

---

## 5. Controlled Error & Status Mapping

### Error Code Mapping (`ExchangeErrorType`)
Bybit error codes (`retCode`) are mapped to the exchange-independent `ExchangeErrorType` enum inside `TradingBot.Application`:

| Bybit Code | Description | Internal Mapped Category |
| :--- | :--- | :--- |
| `10001, 10017, 3400099, 110043` | Parameter or validation error | `ExchangeErrorType.InvalidRequest` |
| `10003, 10004, 10005` | Auth failure or bad signature / expired | `ExchangeErrorType.AuthenticationFailed` |
| `10018, 33004` | Rate limit hit | `ExchangeErrorType.RateLimited` |
| `110004, 110007, 110012, 175003` | Balance issues / insufficient margins | `ExchangeErrorType.InsufficientBalance` |
| `10016, 10002, 10010, 3100000` | Engine or exchange unavailable | `ExchangeErrorType.Unavailable` |
| *Otherwise* | Any other unmapped error | `ExchangeErrorType.Unknown` |

### Status Mapping (`OrderStatus`)
Bybit's external order statuses are mapped to internal domain statuses. Any unexpected state is explicitly translated to `OrderStatus.Unknown` to prevent false positive confirmations:

| Bybit Order Status | Mapped Internal Status |
| :--- | :--- |
| `CREATED` | `OrderStatus.Created` |
| `SUBMITTED` | `OrderStatus.Submitted` |
| `NEW` | `OrderStatus.New` |
| `PARTIALLYFILLED` | `OrderStatus.PartiallyFilled` |
| `FILLED` | `OrderStatus.Filled` |
| `CANCELLED` | `OrderStatus.Cancelled` |
| `REJECTED` | `OrderStatus.Rejected` |
| `FAILED` | `OrderStatus.Failed` |
| `PENDING, TRIGGERED, UNTRIGGERED` | `OrderStatus.Pending` |
| `DEACTIVATED` | `OrderStatus.Cancelled` |
| *Other/Unexpected* | `OrderStatus.Unknown` |

---

## 6. Security Principles

1. **Trade/Read Only Permissions:** API Keys configured must only be granted Trade & Read permissions. Withdraw permissions must be disabled.
2. **Dynamic Overrides:** Sensitive API credentials should be loaded via env overrides (`BYBIT_API_KEY` and `BYBIT_SECRET_KEY`) or protected key vaults, never hardcoded.
3. **Redacted Serialization:** Direct logging of full JSON responses from private endpoints is sanitized to prevent credential leakages.

---

## 7. Testing Strategy

1. **Unit Tests (`BybitExecutionAdapterTests`):**
   - Signature validation against deterministic outputs.
   - Dynamic configuration resolution (Mainnet vs Testnet endpoints).
   - Mapping coverage of `OrderRequest` to HTTP payloads.
   - Exact mapping verification of Bybit codes and statuses.
2. **Integration Tests (`BybitExecutionIntegrationTests`):**
   - E2E flow simulation from `TradingExecutionService` down to Mock Http Server.
   - Real testnet submission test gating via environment variable:
     `BYBIT_TESTNET_INTEGRATION=true`
