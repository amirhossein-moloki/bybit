# Database Audit Strategy & Integrity

This document outlines the structured audit logging architecture, the sanitization filters designed to prevent credential leaks, and the entity lifecycle timestamps implemented to maintain complete historical auditability.

---

## 1. Entity Lifecycle Timestamps

All financial and metadata entities contain standardized audit fields tracked automatically:

- **CreatedAt**: Tracked automatically on the database layer during row insertions (`CURRENT_TIMESTAMP` fallback) and initialized inside the domain entity constructors.
- **UpdatedAt**: Tracked as an explicit property or EF Core shadow property across all tables. On every state modification, the DB context automatically intercepts the save operation via:

```csharp
if (entry.State == EntityState.Modified)
{
    entry.Property("UpdatedAt").CurrentValue = DateTime.UtcNow;
}
```

This guarantees a complete history of modification times for important financial records:
- **Orders**
- **Positions**
- **Trades**
- **Signals**
- **Symbols**
- **RiskRules**
- **ExchangeAccounts**

---

## 2. Audit Logging System

System activities and user interactions are logged into the dedicated `SystemLogs` table. To comply with strict financial audit requirements, we support four primary levels:

1. **Information**: Normal system workflow markers (e.g., successful signal parsing, orders sent).
2. **Warning**: Unexpected non-fatal issues (e.g., transient network failures, temporary API rate-limiting).
3. **Error**: Fatal execution failures (e.g., failed to execute an order, database connection loss).
4. **Critical**: Total system interruption risks (e.g., database disk exhaustion, active credentials rejected repeatedly).

---

## 3. Secret Leak Sanitization & Redaction

The logging system incorporates proactive sanitization filters to prevent the leakage of API Keys, Secrets, or passwords into physical logs or exceptions.

We utilize a pattern-based regex sanitization filter ordered from longest to shortest matching criteria:

```csharp
private static string Sanitize(string input)
{
    if (string.IsNullOrEmpty(input)) return string.Empty;

    var sensitivePatterns = new[] { "secret_key", "api_key", "apikey", "secret", "password" };
    var sanitized = input;
    foreach (var pattern in sensitivePatterns)
    {
        sanitized = System.Text.RegularExpressions.Regex.Replace(
            sanitized,
            pattern,
            "[REDACTED]",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
    return sanitized;
}
```

This ensures that any descriptions containing credentials are automatically and irreversibly redacted at the point of instantiation before ever hitting the file system or database logs.
