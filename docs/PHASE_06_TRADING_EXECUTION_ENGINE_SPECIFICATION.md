# PHASE 06 — Trading Execution Engine Specification

## Project
Telegram Signal Trading Bot

## Phase
Phase 06 — Trading Execution Engine

## Status
Planned


# 1. Objective

هدف این فاز پیاده‌سازی موتور اجرای معاملات است.

در فازهای قبلی:

- Phase 03 پیام تلگرام دریافت شد.
- Phase 04 پیام به Signal استاندارد تبدیل شد.
- Phase 05 ریسک معامله بررسی شد.

در این فاز، سیستم باید بتواند یک معامله تایید شده را به سفارش واقعی در Bybit Testnet تبدیل کند.

Flow اصلی:

```
Telegram Signal

        ↓

Signal Parser

        ↓

Risk Management Engine

        ↓

Trading Execution Engine

        ↓

Bybit API

        ↓

Order Created
```

---

# 2. Scope

## Included

- ساخت سفارش معاملاتی
- ارتباط با Bybit Exchange Adapter
- مدیریت Order Lifecycle
- ارسال سفارش Market
- ارسال سفارش Limit
- ثبت نتیجه سفارش
- مدیریت خطاهای Exchange
- هماهنگی با Database
- Event Logging

---

## Not Included

- مدیریت Position
- Trailing Stop
- Partial Close
- تحلیل عملکرد معاملات
- Backtesting

این موارد در Phase 07 و Phase 11 انجام می‌شوند.

---

# 3. Architecture

ساختار کلی:

```
Application Layer

        |

Trading Execution Service

        |

Exchange Interface

        |

Bybit Exchange Adapter

        |

Bybit Unified V5 API
```

---

# 4. Required Modules

ساختار ماژول:

```
Trading.Execution

├── OrderService

├── OrderBuilder

├── ExchangeGateway

├── OrderValidator

├── OrderTracker

├── ExecutionManager

├── RetryHandler

└── ExecutionLogger
```

---

# 5. Trading Execution Flow

مراحل اجرای معامله:


## Step 1

دریافت Trade Request


Example:

```json
{
 "symbol":"BTCUSDT",
 "side":"BUY",
 "quantity":0.01,
 "type":"MARKET"
}
```


---

## Step 2

بررسی نهایی سفارش:


- Symbol معتبر است؟
- Quantity معتبر است؟
- Exchange Available است؟
- Risk Approval وجود دارد؟


---

## Step 3

ساخت Exchange Order


تبدیل:

```
Internal Order Model

        ↓

Bybit Order Request
```

---

## Step 4

ارسال به Bybit


```
Trading Engine

        ↓

Bybit API

        ↓

Exchange Order ID
```

---

## Step 5

ذخیره نتیجه


```
Order Created

        ↓

Database
```

---

# 6. Order Domain Model


مدل سفارش:


```csharp
Order
{
    Id

    SignalId

    ExchangeOrderId

    Symbol

    Side

    OrderType

    Quantity

    Price

    Status

    CreatedAt

    UpdatedAt
}
```

---

# 7. Order Types


## Market Order


برای ورود سریع:


Example:


```
BUY BTCUSDT

Market Price
```


---

## Limit Order


برای ورود در قیمت مشخص:


Example:


```
BUY BTCUSDT

Price:
60000
```


---

# 8. Order Side


مقادیر:


```
BUY

SELL
```


در Futures:


```
LONG

SHORT
```

به Mapping داخلی تبدیل می‌شود.


---

# 9. Order Builder


مسئول ساخت درخواست سفارش.


Input:


```
Trade Request
```


Output:


```
Bybit Create Order Request
```


مثال:


```json
{
"category":"linear",
"symbol":"BTCUSDT",
"side":"Buy",
"orderType":"Market",
"qty":"0.01"
}
```

---

# 10. Exchange Adapter


سیستم نباید مستقیم به Bybit وابسته باشد.


Interface:


```csharp
public interface IExchangeClient
{

    Task<OrderResult> CreateOrder(
        OrderRequest request
    );


    Task<OrderStatus> GetOrder(
        string orderId
    );


    Task<bool> CancelOrder(
        string orderId
    );

}
```


---

# 11. Bybit Integration


استفاده از:


```
Bybit Unified V5 API
```


قابلیت‌ها:


- Create Order
- Query Order
- Cancel Order
- Get Execution Result


---

# 12. Order Validation


قبل از ارسال سفارش بررسی شود:


## Symbol Validation


مثال:


```
BTCUSDT
```

معتبر باشد.


---

## Quantity Validation


بررسی:


- حداقل مقدار سفارش
- Step Size
- Decimal Precision


---

## Price Validation


برای Limit Order:


```
Price > 0
```


---

## Risk Approval Validation


هیچ سفارشی بدون تایید Phase 05 اجرا نشود.


---

# 13. Order Status Management


وضعیت‌ها:


```
Created

        ↓

Submitted

        ↓

New

        ↓

PartiallyFilled

        ↓

Filled
```


حالات خطا:


```
Cancelled

Rejected

Failed
```

---

# 14. Database Requirements


جدول Orders تکمیل می‌شود:


```
Orders


Id

SignalId

ExchangeOrderId

Symbol

Side

OrderType

Quantity

RequestedPrice

ExecutedPrice

Status

ErrorMessage

CreatedAt

UpdatedAt
```

---

# 15. Execution Retry System


برای خطاهای شبکه:


مثال:


```
Attempt 1

    ↓

Failed

    ↓

Retry

    ↓

Attempt 2
```


قوانین:


- محدودیت تعداد Retry
- Backoff Delay
- ثبت خطا


---

# 16. Exchange Error Handling


موارد:


## API Timeout


رفتار:


```
Retry
```


---

## Invalid Parameter


رفتار:


```
Reject Order
```


---

## Insufficient Balance


رفتار:


```
Order Failed
Notify User
```


---

## Rate Limit


رفتار:


```
Delay

Retry
```


---

# 17. Logging Requirements


ثبت:


```
Order Requested

Order Sent

Exchange Response

Order Filled

Order Failed
```


نباید ثبت شود:


```
API Key

Secret
```


---

# 18. Security Requirements


اجباری:


- API Permission فقط Trade
- Withdraw Disabled
- Secret Encryption
- Secure Configuration
- No Sensitive Logging


---

# 19. Testing Requirements


## Unit Tests


تست:


- Order Builder
- Order Validation
- Status Mapping
- Error Handling
- Retry Logic


---

## Integration Tests


با Bybit Testnet:


```
Create Test Order

        ↓

Receive Order ID

        ↓

Check Status
```


---

# 20. Performance Requirements


سیستم باید:


- Async باشد
- چند درخواست همزمان را مدیریت کند
- Connection Reuse داشته باشد
- Timeout مناسب داشته باشد


---

# 21. Deliverables


در پایان Phase 06:


✅ Trading Execution Service

✅ Order Builder

✅ Bybit Order Adapter

✅ Order Validation

✅ Order Tracking

✅ Retry System

✅ Database Integration

✅ Logging

✅ Unit Tests

✅ Integration Tests


---

# 22. Completion Criteria


این فاز زمانی کامل است که:


- یک Signal تایید شده دریافت شود.
- Risk Engine آن را تایید کند.
- سفارش در Bybit Testnet ایجاد شود.
- Exchange Order ID دریافت شود.
- وضعیت سفارش ذخیره شود.
- خطاها مدیریت شوند.


---

# Output


خروجی نهایی Phase 06:


```
Approved Trade Request

        ↓

Trading Execution Engine

        ↓

Bybit Testnet Order

        ↓

Stored Order Record
```


---

# Next Phase

PHASE 07 — Position Management System
```
