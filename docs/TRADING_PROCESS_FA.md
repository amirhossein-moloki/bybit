# راهنمای کامل و بصری روند ترید (Trading Process Engine)

این مستند به طور کامل و تصویری، روند معامله‌گری خودکار (Trading Process) را در این سیستم شرح می‌دهد. تمام مراحل از زمان دریافت پیام در تلگرام تا تحلیل سود/زیان نهایی به همراه دیاگرام‌های **Mermaid** و توضیحات فنی دقیق آورده شده‌اند.

---

## ۱. نگاه کلی به معماری و جریان داده سیستم

سیستم از معماری Clean Architecture و Domain-Driven Design (DDD) پیروی می‌کند و جریان معاملات طی ۶ مرحله اصلی مدیریت می‌شود:

```mermaid
graph TD
    A[۱. تلگرام: دریافت پیام] --> B[۲. پردازش و استخراج سیگنال]
    B --> C[۳. ارزیابی ریسک و تعیین حجم]
    C --> D[۴. ایجاد و اجرای سفارش در صرافی]
    D --> E[۵. مدیریت و محافظت از پوزیشن]
    E --> F[۶. بستن پوزیشن و ثبت تحلیل سود/زیان]

    style A fill:#e1f5fe,stroke:#0288d1,stroke-width:2px
    style B fill:#fff9c4,stroke:#fbc02d,stroke-width:2px
    style C fill:#ffe0b2,stroke:#f57c00,stroke-width:2px
    style D fill:#f8bbd0,stroke:#c2185b,stroke-width:2px
    style E fill:#d1c4e9,stroke:#512da8,stroke-width:2px
    style F fill:#c8e6c9,stroke:#388e3c,stroke-width:2px
```

---

## ۲. مرحله اول: دریافت پیام از تلگرام (Telegram Ingestion)

1. **دریافت (Reception):** سرویس `TelegramListenerWorker` از طریق کتابخانه `WTelegramClient` پیام‌های کانال‌های تحت نظر را دریافت می‌کند.
2. **صف‌بندی (Queueing):** پیام‌ها درون صف ایمن thread-safe قرار می‌گیرند (`SignalStorageQueue`).
3. **ذخیره‌سازی و یکتاپذیری (Persistence & Deduplication):** سرویس `SignalStorageWorker` پیام را در دیتابیس (`TelegramMessages`) ذخیره می‌کند. در صورتی که پیام تکراری باشد (ترکیب `ChannelId` + `MessageId`)، به وسیله کلید یکتا (Unique Index) دیتابیس رد می‌شود.

```mermaid
sequenceDiagram
    autonumber
    participant Channel as کانال تلگرام
    participant Listener as TelegramListenerWorker
    participant Queue as SignalStorageQueue
    participant Worker as SignalStorageWorker
    participant DB as دیتابیس (PostgreSQL)

    Channel->>Listener: دریافت پیام جدید
    Listener->>Queue: افزودن پیام به صف
    Queue->>Worker: برداشت پیام از صف
    Worker->>DB: بررسی یکتایی و ذخیره در TelegramMessages
    alt پیام تکراری است
        DB-->>Worker: خطا / کلید تکراری
        Worker->>Worker: نادیده گرفتن پیام
    else پیام جدید است
        DB-->>Worker: ذخیره موفق
        Worker->>Worker: ارسال به مرحله بعد (Parsing)
    end
```

---

## ۳. مرحله دوم: پردازش، دسته‌بندی و استخراج سیگنال (Message Parsing)

در این مرحله پیام خام پردازش می‌شود:

1. **دسته‌بندی (Classification):** کلاس `MessageClassifier` بر اساس واژگان کلیدی انگلیسی و فارسی پیام را در یکی از گروه‌های زیر قرار می‌دهد:
   - `Signal` (سیگنال جدید)
   - `TradeUpdate` (به‌روزرسانی معامله مانند تغییر حد ضرر)
   - `Cancel` (لغو سیگنال)
   - `Chat` (چت معمولی - نادیده گرفته می‌شود)
2. **استخراج الگو محور (Template Mode):** پیام با الگوهای Regex ذخیره شده در دیتابیس مقایسه می‌شود.
3. **استخراج با هوش مصنوعی (AI Fallback Mode):** اگر الگویی منطبق نشود، پیام به ماژول AI (`AIAnalyzer`) ارسال شده تا پارامترهای سیگنال با خروجی ساختاریافته JSON استخراج شوند.
4. **اعتبارسنجی اولیه (Validation):** سرویس `SignalValidationService` پر بودن نماد (Symbol)، جهت معامله (Buy/Sell)، نقطه ورود (Entry)، حد ضرر (SL) و اهداف سود (TP) را بررسی می‌کند.

```mermaid
flowchart TD
    A[پیام خام تلگرام] --> B{دسته‌بندی پیام}
    B -- Chat --> C[حذف / نادیده گرفتن]
    B -- TradeUpdate/Cancel --> D[اعمال تغییرات روی پوزیشن موجود]
    B -- Signal --> E{مطابقت با Pattern/Template؟}

    E -- بله --> F[استخراج الگو محور]
    E -- خیر --> G[ارسال به AI Analyzer / LLM]

    F --> H[اعتبارسنجی ساختار سیگنال]
    G --> H

    H -- نامعتبر --> I[ثبت خطای Validation و رد سیگنال]
    H -- معتبر --> J[ارسال سیگنال به موتور ریسک]
```

---

## ۴. مرحله سوم: مدیریت ریسک و تعیین حجم (Risk Engine & Sizing)

قبل از ارسال هر سفارشی به صرافی، سیگنال باید از ۹ قانون ریسک (Risk Rules) بگذرد:

1. **ارزیابی قوانین ریسک (Risk Rule Evaluation):**
   - بررسی حداکثر افت سرمایه (Max Drawdown)
   - حداکثر حد ضرر روزانه (Max Daily Loss)
   - حداکثر ریسک در هر معامله (Risk Per Trade)
   - حداکثر مارجین درگیر (Max Margin Exposure)
   - حداکثر لوریج مجاز (Max Leverage)
   - و سایر قوانین حفاظتی
2. **تعیین حجم معامله (Position Sizing):** محاسبه دقیق میزان حجم (Quantity) بر اساس درصد ریسک حساب، فاصله نقطه ورود تا حد ضرر و اهرم (Leverage).
3. **اعتبارسنجی قوانین صرافی (Instrument Rules):** تطبیق حجم و قیمت با قوانین صرافی (حداقل/حداکثر حجم، Step Size، Tick Size و Min Notional Value).

```mermaid
flowchart LR
    A[سیگنال معتبر] --> B[موتور ارزیابی ریسک]
    subgraph Rules [قوانین ۹ گانه ریسک]
        B1[Drawdown Rule]
        B2[Daily Loss Rule]
        B3[Margin Exposure]
        B4[Leverage Check]
    end
    B --> Rules
    Rules -- رد ریسک --> C[لغو معامله و ثبت لوگ]
    Rules -- تایید ریسک --> D[محاسبه حجم معامله Position Sizing]
    D --> E[تطبیق با Instrument Rules صرافی]
    E --> F[سفارش آماده ارسال به صرافی]
```

---

## ۵. مرحله چهارم: اجرای سفارش در صرافی (Order Execution Engine)

سفارش تایید شده توسط موتور ریسک، وارد ماشین حالت سفارش (Order State Machine) می‌شود:

1. **شناسه یکتا (ClientOrderId):** یک شناسه یکتای یکتاپذیر (Idempotent) با فرمت `TB-{Guid}` ایجاد می‌شود.
2. **امضای درخواست (HMAC-SHA256):** درخواست به صورت امن امضا شده و به API V5 صرافی بايبیت (Bybit) ارسال می‌گردد.
3. **تغییرات حالت سفارش (Order Status Machine):**

```mermaid
stateDiagram-v2
    [*] --> Created : ساخت سفارش اولیه
    Created --> Submitted : ارسال به صرافی
    Submitted --> Accepted : پذیرش توسط صرافی (دریافت ExchangeOrderId)
    Submitted --> Rejected : رد سفارش توسط صرافی
    Accepted --> PartiallyFilled : پر شدن بخشی از سفارش
    Accepted --> Filled : پر شدن کامل سفارش
    PartiallyFilled --> Filled : پر شدن مابقی سفارش
    Accepted --> Cancelled : لغو توسط کاربر/سیستم
    Filled --> [*]
    Rejected --> [*]
    Cancelled --> [*]
```

---

## ۶. مرحله پنجم: مدیریت و محافظت از پوزیشن (Position Lifecycle & Protection)

پس از پر شدن سفارش (`Filled`)، پوزیشن در حالت `Open` قرار می‌گیرد. در این مرحله سیستم به صورت لحظه‌ای از طریق **WebSocket** و **REST Sync** پوزیشن را مدیریت می‌کند:

```mermaid
flowchart TD
    A[پوزیشن باز شد Open] --> B[تنظیم حد ضرر SL و حد سود TP اولیه در صرافی]
    B --> C{پایش لحظه‌ای قیمت WebSocket}

    C -->|قیمت به نقطه Break-Even رسید| D[تغییر حد ضرر به قیمت ورود + کارمزد]
    C -->|قیمت به TP1 / TP2 رسید| E[خروج پله‌ای Partial Close]
    C -->|فعال شدن Trailing Stop| F[تعقیب قیمت و بروزرسانی دینامیک SL]
    C -->|برخورد قیمت به SL یا تمام TPها| G[بستن کامل پوزیشن Closed]

    D --> C
    E --> C
    F --> C
    G --> H[انتقال پوزیشن به جدول معاملات نهایی Trades]
```

### قابلیت‌های پیشرفته محافظت از پوزیشن:
* **نقطه سر به سر (Break-Even):** وقتی قیمت به تارگت مشخصی برسد، حد ضرر به قیمت ورود (به همراه جبران کارمزد) منتقل می‌شود تا ریسک معامله صفر شود.
* **اهداف چندگانه سود (Multi-TP):** با رسیدن قیمت به هر تارگت، درصدی از حجم معامله نقد شده و حد ضرر تارگت‌های بعدی جابه‌جا می‌شود.
* **حد ضرر متحرک (Trailing Stop):** حد ضرر با فاصله ثابت یا درصدی، به دنبال قیمت حرکت می‌کند تا بیشترین سود ممکن حفظ شود.
* **همگام‌سازی خودکار (Self-Healing Loop):** سرویس‌های پس‌زمینه با صرافی همگام می‌شوند تا در صورت قطعی اینترنت یا وب‌سوکت، وضعیت پوزیشن‌ها دائماً بازیابی و اصلاح شود.

---

## ۷. مرحله ششم: ثبت معامله و تحلیل عملکرد (Analytics & Reporting)

پس از بسته شدن پوزیشن، اطلاعات معامله در جدول `Trades` ثبت شده و شاخص‌های آماری محاسبه می‌شوند:

```mermaid
gantt
    title نمونه چرخه زمانی یک معامله (Trade Timeline)
    dateFormat  HH:mm
    axisFormat %H:%M

    دریافت پیام تلگرام       :a1, 10:00, 1m
    پردازش و ریسک           :a2, after a1, 1m
    ارسال سفارش به صرافی    :a3, after a2, 1m
    پوزیشن باز (Open)       :a4, after a3, 15m
    تریلینگ استاپ & BE      :a5, after a4, 20m
    بسته شدن پوزیشن (Closed):a6, after a5, 1m
    ثبت در Analytics        :a7, after a6, 1m
```

### شاخص‌های محاسباتی در بخش Analytics:
* **PnL خالص و ناخالص (Gross & Net Realized PnL)**
* **نرخ برد (Win Rate %)**
* **ضریب سودآوری (Profit Factor)**
* **منحنی افت سرمایه (Drawdown Curve)**
* **میانگین زمان باز بودن معاملات (Average Duration)**

---

## ۸. دیاگرام جامع و کامل چرخه معامله (End-to-End Sequence Diagram)

دیاگرام زیر تمامی اجزای سیستم را در یک نگاه نشان می‌دهد:

```mermaid
sequenceDiagram
    autonumber
    actor User as کانال تلگرام
    participant TG as Telegram Service
    participant Parser as Signal Parser & AI
    participant Risk as Risk & Size Engine
    participant Exec as Execution Engine
    participant Exchange as صرافی بایت‌بیت (Bybit)
    participant PosMgr as Position Protection Manager
    participant DB as دیتابیس & Analytics

    User->>TG: ارسال پیام سیگنال خرید
    TG->>DB: ذخیره پیام خام
    TG->>Parser: ارسال برای استخراج
    Parser->>Parser: تطبیق الگوی Regex / AI LLM
    Parser-->>Risk: سیگنال ساختاریافته (Symbol, Side, Entry, SL, TPs)
    Risk->>Risk: ارزیابی قوانین ۹ گانه ریسک + محاسبه حجم (Sizing)
    alt رد ریسک
        Risk-->>DB: ثبت علت رد سیگنال
    else تایید ریسک
        Risk->>Exec: سفارش آماده ارسال
        Exec->>Exchange: REST: ثبت سفارش با ClientOrderId
        Exchange-->>Exec: سفارش پذیرفته شد (Accepted)
        Exchange-->>PosMgr: WebSocket: رویداد Executed/Filled
        PosMgr->>Exchange: REST: تنظیم حد ضرر (SL) و حد سودها (TP)
        loop پایش قیمت (WebSocket Stream)
            Exchange-->>PosMgr: آپدیت قیمت لحظه‌ای
            opt تحریک Break-Even / Trailing Stop
                PosMgr->>Exchange: REST: جابه‌جایی حد ضرر
            end
        end
        Exchange-->>PosMgr: WebSocket: حد ضرر یا حد سود خرد شد (Position Closed)
        PosMgr->>DB: ثبت معامله نهایی در جدول Trades
        DB->>DB: بروزرسانی شاخص‌های Analytics (PnL, WinRate, Drawdown)
    end
```

---
*این مستند مطابق با آخرین کد و معماری پیاده‌سازی شده در پروژه Trading Bot آماده شده است.*
