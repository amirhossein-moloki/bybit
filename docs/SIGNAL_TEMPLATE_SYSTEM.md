# Signal Template Parsing System

The **Signal Template Parsing System** is an enterprise-grade, highly customizable, and decoupled template-driven rule engine designed to support multiple Telegram channel message formats without modifying core parser or extractor code. It provides multi-format parsing, priority-based matching, database-backed template persistence, and thread-safe execution isolation.

---

## 1. System Architecture

The pipeline processes Telegram channel messages as follows:

```
    Telegram Message Received (ParserContext)
                     ↓
          ITemplateManager (FindTemplateAsync)
                     ↓
        Channel ID Check & Priority Matching
                     ↓
          TemplateMatcher Selection
                     ↓
           Selected ISignalTemplate
                     ↓
     Thread-Local Isolation (TemplateContext)
                     ↓
       Sequential Extractor Execution (IExtractors)
                     ↓
          Required Rule Verifications
                     ↓
         ParserResult (Success or Failure)
```

By decoupling extractor matching patterns from hardcoded string literals to dynamic patterns retrieved from matched templates, the core parser pipeline remains closed to modifications but open to extension (Open-Closed Principle).

---

## 2. Template Architecture Components

The template system is composed of the following decoupled classes under `TradingBot.Parser/Templates/`:

1. **`ISignalTemplate`**: Represents the interface defining behavior for matching contexts (`CanHandle`) and retrieving template rules.
2. **`SignalTemplate`**: The primary implementation of `ISignalTemplate`. It contains metadata such as ID, Name, ChannelId, Priority, and the parsed rules.
3. **`TemplateRule`**: Represents an extraction rule for a specific field, specifying the pattern to look for, the extractor to use, and whether the field is strictly required.
4. **`TemplateMatcher`**: A static rule matcher that matches and scores candidates, prioritised as:
   `Channel-Specific Template (sorted by Priority then CreatedAt) > Generic Template (sorted by Priority then CreatedAt)`
5. **`TemplateManager`**: Coordinates loading enabled templates from database storage (via `IParserTemplateRepository`) and falling back to a pre-defined fallback template (`DefaultSignalTemplate`).
6. **`TemplateContext`**: Implements thread-safe and asynchronous execution context-safe isolation using `System.Threading.AsyncLocal` to store the active template for the currently executing parsing flow.

---

## 3. Template Rule Schema Format

Each template stores its custom field parsing rules as a serialized JSON array inside the `ConfigurationJson` field of the database entity `ParserTemplates`.

### Example JSON Schema

```json
[
  {
    "Field": "Symbol",
    "Pattern": "",
    "Extractor": "SymbolExtractor",
    "Required": true,
    "Order": 1
  },
  {
    "Field": "Side",
    "Pattern": "",
    "Extractor": "DirectionExtractor",
    "Required": true,
    "Order": 2
  },
  {
    "Field": "EntryPrice",
    "Pattern": "BUY AREA",
    "Extractor": "EntryExtractor",
    "Required": true,
    "Order": 3
  },
  {
    "Field": "StopLoss",
    "Pattern": "STOP",
    "Extractor": "StopLossExtractor",
    "Required": true,
    "Order": 4
  },
  {
    "Field": "TakeProfits",
    "Pattern": "TARGET",
    "Extractor": "TakeProfitExtractor",
    "Required": true,
    "Order": 5
  }
]
```

### Properties Definition

| Property | Type | Description |
| :--- | :--- | :--- |
| `Field` | `string` | The target `ParsedSignal` property name (e.g., `Symbol`, `Side`, `EntryPrice`, `StopLoss`, `TakeProfits`, `Leverage`). |
| `Pattern` | `string` | The target match keyword (e.g., `"Entry:"`, `"BUY AREA"`, `"STOP"`, `"TP|TARGET"`). Regular expression alternations or literals are supported. |
| `Extractor` | `string` | The target class responsible for executing extraction (e.g., `"SymbolExtractor"`, `"EntryExtractor"`, `"StopLossExtractor"`, `"TakeProfitExtractor"`). |
| `Required` | `bool` | If set to `true`, the pipeline validates that a value was successfully extracted. If missing, it appends a Warning to the final `ParserResult` without breaking pipeline execution. |
| `Order` | `int` | Dictates sequential processing order. |

---

## 4. Channel Mapping and Matching Logic

The matching workflow utilizes the following algorithm:

1. **Enabled Checks**: Ensure the template is marked as `Enabled = true`.
2. **Channel-Specific Check**: If the template has a configured `ChannelId`, verify if the incoming context's `SourceChannel` matches. Channel-specific matches take immediate priority.
3. **Generic Template Pattern Check**: If no channel-specific template matches, generic templates (where `ChannelId` is null) are evaluated. A generic template matches if the raw signal text contains any of its rule patterns.
4. **Fallback Default**: If no matching template is resolved from the database, the engine falls back to `DefaultSignalTemplate` which handles standard universal signal structures.

---

## 5. Persistence Storage Strategy

The database table `ParserTemplates` stores persistent custom configurations.

### Entity Model

```csharp
public class ParserTemplates
{
    public Guid Id { get; set; }
    public string Name { get; set; }
    public long? ChannelId { get; set; }
    public string ConfigurationJson { get; set; }
    public bool Enabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
```

### Table Schema and Indexes

Optimistic Concurrency checking is configured via EF Fluent API mapping `UpdatedAt` as a concurrency token.

```csharp
builder.ToTable("ParserTemplates");
builder.HasKey(x => x.Id);
builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
builder.Property(x => x.ConfigurationJson).HasColumnType("text").IsRequired();
builder.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
builder.Property(x => x.UpdatedAt).IsConcurrencyToken();
builder.HasIndex(x => x.ChannelId);
```

---

## 6. Extension Strategy

To support a new Telegram channel format without modifying any source code:

1. **Define the Template JSON**: Write a customized template rules array.
2. **Add to Database**: Insert a new row into the `ParserTemplates` table matching the target Telegram channel ID:
   ```sql
   INSERT INTO "ParserTemplates" ("Id", "Name", "ChannelId", "ConfigurationJson", "Enabled", "CreatedAt")
   VALUES (
       'a1b2c3d4-e5f6-7a8b-9c0d-1e2f3a4b5c6d',
       'Premium VIP Channel',
       987654321,
       '[{"Field":"EntryPrice","Pattern":"BUY REGION","Extractor":"EntryExtractor","Required":true,"Order":3}]',
       true,
       NOW()
   );
   ```
3. **Execution**: The pipeline immediately picks up the new channel template on the next incoming message, applying the `"BUY REGION"` pattern for entry price extraction automatically.
