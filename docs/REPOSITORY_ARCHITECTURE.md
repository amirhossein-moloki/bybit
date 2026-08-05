# Repository Architecture

The database access layer of the **Telegram Signal Trading Bot** has been fully restructured using the **Repository Pattern** to ensure complete decoupling of the Application/Domain layers from direct dependencies on Entity Framework Core and PostgreSQL.

---

## 1. Directory Structure

```text
src/
TradingBot.Application
└── Repositories
    ├── IRepository.cs            # Generic async CRUD & Specification Repository interface
    ├── IOrderRepository.cs       # Specialized Order repository interface
    ├── ISignalRepository.cs      # Specialized Signal repository interface
    ├── IPositionRepository.cs    # Specialized Position repository interface
    ├── ITradeRepository.cs       # Specialized Trade repository interface
    ├── IUnitOfWork.cs            # Unit of Work transaction interface
    ├── PagedResult.cs            # Standard offset-pagination container
    ├── ProfitLossReport.cs       # Custom trading performance report model
    ├── ISpecification.cs         # Specification criteria interface
    └── BaseSpecification.cs     # Abstract base for Specification queries

TradingBot.Persistence
├── Repositories
│   ├── RepositoryBase.cs         # EF Core generic base implementation
│   ├── OrderRepository.cs        # EF Core specialized Order query implementation
│   ├── SignalRepository.cs       # EF Core specialized Signal query implementation
│   ├── PositionRepository.cs     # EF Core specialized Position query implementation
│   └── TradeRepository.cs        # EF Core specialized Trade query implementation
└── UnitOfWork
    └── UnitOfWork.cs             # EF Core concrete transaction manager
```

---

## 2. Abstractions (`IRepository<T>`)

Common asynchronous database operations are declared in `IRepository<T>`:

```csharp
public interface IRepository<T> where T : class
{
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<T>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<T>> GetAsync(ISpecification<T> spec, CancellationToken cancellationToken = default);
    Task<PagedResult<T>> GetPagedAsync(int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task<PagedResult<T>> GetPagedAsync(ISpecification<T> spec, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
    Task AddAsync(T entity, CancellationToken cancellationToken = default);
    void Update(T entity);
    void Remove(T entity);
}
```

---

## 3. Query Optimization (`AsNoTracking()`)

- **Read Operations:** All read-only methods (such as lists, history, pagination, and reporting queries) execute with `.AsNoTracking()` enabled. This bypasses EF Core's change tracking state-machine, offering major performance improvements and lower memory overhead.
- **Write Operations:** Read operations intended for updates (such as state changes) utilize EF's tracking features to allow atomic updates via `Update(T entity)`.

---

## 4. Specification Pattern

For complex queries, filtering, and eager loading, the **Specification Pattern** is utilized:
- **`ISpecification<T>`** specifies criteria expression, eager-loaded includes, and ordering expressions.
- **`BaseSpecification<T>`** provides a clean, reusable base implementation of specifications.

---

## 5. Pagination Support

To maintain database efficiency with large datasets, offset pagination is supported for `Orders`, `Trades`, and `Signals` using the generic `PagedResult<T>` container:

```csharp
public class PagedResult<T>
{
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public IEnumerable<T> Items { get; set; }
}
```
The specialized repositories offer high-performance paginated queries via `GetPagedOrdersAsync`, `GetPagedSignalsAsync`, and `GetPagedTradesAsync`.
