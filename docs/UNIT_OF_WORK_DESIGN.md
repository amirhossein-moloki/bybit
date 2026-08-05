# Unit of Work Design

The **Unit of Work** pattern coordinates atomic transaction boundaries and manages business operation outcomes within the **Telegram Signal Trading Bot**. It guarantees that all database updates succeeding a trading trigger commit atomically, or roll back entirely in case of failure.

---

## 1. Interface (`IUnitOfWork`)

```csharp
public interface IUnitOfWork : IDisposable
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    // Original transaction methods (retained for backward compatibility)
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);

    // New transaction methods
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
```

---

## 2. Safe Exception Handling & Wrapping

To comply with high-availability enterprise standards:
1. **`DatabaseException`** wraps any standard EF Core `DbUpdateException` or unexpected database engine failure inside `SaveChangesAsync()`.
2. **`TransactionException`** wraps any failures occurring during transaction operations (`BeginTransactionAsync`, `CommitAsync`, `RollbackAsync`).

### Highlights:
- **Preserve original exception:** The original Exception is always passed down as `InnerException`.
- **Log safely:** Sensitive information (such as credentials, internal hostnames, and database schemas) is stripped before presenting errors, while complete details are logged safely to the server's diagnostic log using `ILogger`.

---

## 3. Transaction Workflow

An execution workflow consists of:

```text
       Start Transaction
               ↓
          Create Order
               ↓
           Save Order
               ↓
         Update Position
               ↓
    Commit Transaction (Success)
               OR
     Rollback Everything (Failure)
```

### Implementation Example:

```csharp
await _unitOfWork.BeginTransactionAsync(cancellationToken);
try
{
    var order = new Order(...);
    await _orderRepository.AddAsync(order, cancellationToken);
    await _unitOfWork.SaveChangesAsync(cancellationToken);

    var position = new Position(...);
    await _positionRepository.AddAsync(position, cancellationToken);
    await _unitOfWork.SaveChangesAsync(cancellationToken);

    await _unitOfWork.CommitAsync(cancellationToken);
}
catch (Exception)
{
    await _unitOfWork.RollbackAsync(cancellationToken);
    throw;
}
```
This guarantees no partial financial records are left in the database.
