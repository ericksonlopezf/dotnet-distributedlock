# API Reference — EricksonLopez.DistributedLock

> **Formal Technical Specification & Microsoft Learn Style Reference Manual**  
> **Version**: 1.0.0 | **Ecosystem**: `EricksonLopez.*` | **Target Runtimes**: .NET 8.0, .NET 9.0, .NET 10.0

---

## 1. Namespace: `EricksonLopez.DistributedLock.Abstractions`

The foundational Tier 0 package providing pure contracts, error definitions, declarative attributes, and high-level safe scope extension methods.

### `IDistributedLockProvider` (Interface)

Unified provider contract for acquiring distributed mutual exclusion handles across all supported storage backends.

```csharp
namespace EricksonLopez.DistributedLock.Abstractions;

public interface IDistributedLockProvider
{
    Task<Result<IAsyncDisposable>> TryAcquireAsync(
        string resourceId,
        CancellationToken cancellationToken = default);

    Task<Result<IAsyncDisposable>> TryAcquireAsync(
        string resourceId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task<Result<IAsyncDisposable>> AcquireAsync(
        string resourceId,
        CancellationToken cancellationToken = default);

    Task<Result<IAsyncDisposable>> AcquireAsync(
        string resourceId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task<Result<IDistributedLockHandle>> TryAcquireHandleAsync(
        string resourceId,
        CancellationToken cancellationToken = default);

    Task<Result<IDistributedLockHandle>> TryAcquireHandleAsync(
        string resourceId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    Task<Result<IDistributedLockHandle>> AcquireHandleAsync(
        string resourceId,
        CancellationToken cancellationToken = default);

    Task<Result<IDistributedLockHandle>> AcquireHandleAsync(
        string resourceId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
```

#### Method Details

##### `TryAcquireAsync(string resourceId, CancellationToken cancellationToken = default)`
Attempts an immediate, non-blocking lock acquisition for the specified resource.
- **Parameters**:
  - `resourceId`: Non-null, non-whitespace unique resource identifier key.
  - `cancellationToken`: Cooperative token to cancel the operation.
- **Returns**: `Result<IAsyncDisposable>` containing the disposable lock handle on success; or `DistributedLockErrors.LockAlreadyHeld` if contested, or `DistributedLockErrors.Canceled` if cancelled.
- **Exceptions**: `ArgumentException` if `resourceId` is null, empty, or whitespace.

##### `TryAcquireAsync(string resourceId, TimeSpan timeout, CancellationToken cancellationToken = default)`
Attempts to acquire the lock within the specified timeout using jittered exponential polling.
- **Parameters**:
  - `resourceId`: Unique resource identifier key.
  - `timeout`: Maximum duration to wait before aborting.
  - `cancellationToken`: Cooperative cancellation token.
- **Returns**: `Result<IAsyncDisposable>` on success; or `DistributedLockErrors.Timeout` if duration expires.

##### `AcquireAsync(string resourceId, CancellationToken cancellationToken = default)`
Blocks asynchronously until the lock is acquired, delegating to native database blocking queues where supported (`pg_advisory_lock`, `sp_getapplock`).

##### `TryAcquireHandleAsync(...)` & `AcquireHandleAsync(...)`
Strongly typed variants returning `Result<IDistributedLockHandle>`, exposing `HandleLostToken`, `ResourceId`, `LockId`, and `FencingToken`.

---

### `IDistributedLockHandle` (Interface)

Represents an active, held distributed lock. Extends `IAsyncDisposable`.

```csharp
namespace EricksonLopez.DistributedLock.Abstractions;

public interface IDistributedLockHandle : IAsyncDisposable
{
    CancellationToken HandleLostToken { get; }
    string ResourceId { get; }
    long LockId { get; }
    long? FencingToken => null; // Default interface implementation — returns null unless overridden
}
```

- **`HandleLostToken`**: Triggered when the lock is lost due to unexpected TCP socket disconnects, process crashes, or backend failover.
- **`ResourceId`**: The original string key supplied during acquisition.
- **`LockId`**: Deterministic 64-bit integer hash generated via SHA-256 for database engines requiring numeric identifiers.
- **`FencingToken`**: Optional monotonically increasing sequence token for Kleppmann storage write validation.
- **`DisposeAsync()`**: Asynchronously releases the lock in the backend and closes/returns the underlying connection to the pool.

---

### `DistributedLockExtensions` (Static Class)

High-level safe execution scopes that manage disposal and cooperative cancellation linking automatically.

```csharp
namespace EricksonLopez.DistributedLock.Abstractions;

public static class DistributedLockExtensions
{
    // Non-blocking, no return value
    public static Task<Result<bool>> ExecuteWithLockAsync(
        this IDistributedLockProvider provider,
        string resourceId,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default);

    // Non-blocking, with return value
    public static Task<Result<T>> ExecuteWithLockAsync<T>(
        this IDistributedLockProvider provider,
        string resourceId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default);

    // With timeout, no return value
    public static Task<Result<bool>> ExecuteWithLockAsync(
        this IDistributedLockProvider provider,
        string resourceId,
        TimeSpan timeout,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default);

    // With timeout AND return value
    public static Task<Result<T>> ExecuteWithLockAsync<T>(
        this IDistributedLockProvider provider,
        string resourceId,
        TimeSpan timeout,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default);
}
```

---

### `DistributedLockErrors` (Static Class)

Pre-allocated, immutable error instances derived from `EricksonLopez.Result.Error`:

| Error Instance | Code | Type | Description |
|---|---|:---:|---|
| `DistributedLockErrors.LockAlreadyHeld` | `"DistributedLock.AlreadyHeld"` | Conflict | The requested resource is held by another process or connection. |
| `DistributedLockErrors.Timeout` | `"DistributedLock.Timeout"` | Failure | Acquisition elapsed the specified timeout duration without obtaining the lock. |
| `DistributedLockErrors.LockLost` | `"DistributedLock.Lost"` | Failure | The underlying connection or lease terminated unexpectedly during execution. |
| `DistributedLockErrors.Canceled` | `"DistributedLock.Canceled"` | Failure | Acquisition was aborted via `CancellationToken`. |

---

### `DistributedLockAttribute` (Sealed Class)

Declarative attribute for tagging MediatR commands, MassTransit consumers, or application endpoints with locking requirements:

```csharp
namespace EricksonLopez.DistributedLock.Abstractions;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class DistributedLockAttribute : Attribute
{
    public DistributedLockAttribute(string resourceKeyPattern);
    public string ResourceKeyPattern { get; }
    public int TimeoutSeconds { get; set; } = 0;
    public bool Blocking { get; set; } = false;
}
```

---

### `DistributedLockMetrics` (Static Class)

Native OpenTelemetry instrumentation using `System.Diagnostics.Metrics.Meter`:

- **Meter Name**: `"EricksonLopez.DistributedLock"`
- **Meter Version**: `"1.0.0"`
- **Methods**:
  - `RecordAcquisition(string resourceId, string lockType, string status)`
  - `RecordWaitDuration(string resourceId, string lockType, string status, double durationMs)`
  - `RecordHoldDuration(string resourceId, string lockType, double durationMs)`
  - `RecordLockLost(string resourceId, long lockId)`

---

## 2. Dependency Injection Registration APIs

Each concrete dialect package registers `IDistributedLockProvider` into `IServiceCollection` via idiomatic extension methods:

### PostgreSQL (`EricksonLopez.DistributedLock.PostgreSql`)
```csharp
// Factory-based registration (supports NpgsqlDataSource, Npgsql, or any DbConnection factory)
services.AddPostgresDistributedLock(
    sp => new NpgsqlConnection(connectionString),
    options =>
    {
        options.CommandTimeoutSeconds = 15;
        options.KeepaliveCadence = TimeSpan.FromSeconds(5);
        options.InitialPollingInterval = TimeSpan.FromMilliseconds(50);
        options.MaxPollingInterval = TimeSpan.FromMilliseconds(250);
        options.JitterRatio = 0.25;
    });
```

### SQL Server (`EricksonLopez.DistributedLock.SqlServer`)
```csharp
// Factory-based registration
services.AddSqlServerDistributedLock(
    () => new SqlConnection(connectionString),
    options =>
    {
        options.CommandTimeoutSeconds = 30;
        options.KeepaliveCadence = TimeSpan.FromSeconds(10);
        options.RetryInterval = TimeSpan.FromMilliseconds(50);
        options.BackoffJitter = true;
    });

// Or connection string overload (creates SqlConnection internally)
services.AddSqlServerDistributedLock(connectionString);
```

### MySQL (`EricksonLopez.DistributedLock.MySql`)
```csharp
services.AddMySqlDistributedLock(
    connectionString,
    options =>
    {
        options.CommandTimeoutSeconds = 15;
        options.RetryInterval = TimeSpan.FromMilliseconds(100);
        options.BackoffJitter = true;
    });
```

### MariaDB (`EricksonLopez.DistributedLock.MariaDb`)
```csharp
services.AddMariaDbDistributedLock(
    connectionString,
    options =>
    {
        options.CommandTimeoutSeconds = 15;
        options.RetryInterval = TimeSpan.FromMilliseconds(100);
        options.BackoffJitter = true;
    });
```

### Oracle (`EricksonLopez.DistributedLock.Oracle`)
```csharp
services.AddOracleDistributedLock(
    connectionString,
    options =>
    {
        options.CommandTimeoutSeconds = 30;
        options.RetryInterval = TimeSpan.FromMilliseconds(200);
        options.BackoffJitter = true;
    });
```

### SQLite (`EricksonLopez.DistributedLock.Sqlite`)
```csharp
services.AddSqliteDistributedLock(
    "Data Source=app_coordination.db;",
    options =>
    {
        options.LockTtl = TimeSpan.FromSeconds(60);
        options.RetryInterval = TimeSpan.FromMilliseconds(50);
        options.BackoffJitter = true;
    });
```

### Redis (`EricksonLopez.DistributedLock.Redis`)
```csharp
// Explicit multiplexer registration
services.AddRedisDistributedLock(
    connectionMultiplexer,
    options =>
    {
        options.DefaultExpiry = TimeSpan.FromSeconds(30);
        options.KeepaliveCadence = TimeSpan.FromSeconds(10); // Automated lease renewal
        options.KeyPrefix = "locks:";
        options.RetryInterval = TimeSpan.FromMilliseconds(50);
        options.BackoffJitter = true;
    });

// Service-provider-resolved multiplexer (IConnectionMultiplexer must be registered separately)
services.AddRedisDistributedLock(options =>
{
    options.DefaultExpiry = TimeSpan.FromSeconds(30);
    options.KeepaliveCadence = TimeSpan.FromSeconds(10);
});
```

---

## 3. Transactional Lock Extensions

Available in relational packages (`PostgreSql`, `SqlServer`, `MySql`, `MariaDb`, `Oracle`, `Sqlite`), allowing locks to be acquired directly within existing database transactions. Locks automatically release when the transaction issues `Commit()` or `Rollback()`, eliminating lock leakage in PgBouncer transaction-mode pools.

Each dialect exposes a static extension class (e.g., `SqliteTransactionLockExtensions`, `PostgresTransactionLockExtensions`). The full overload surface per dialect is:

```csharp
// Example shown for Sqlite — all relational dialects expose the same overload surface
public static class SqliteTransactionLockExtensions
{
    // ── Non-Blocking ─────────────────────────────────────────────────────────────
    // Attempts immediately; returns LockAlreadyHeld if contested
    public static Task<Result<IDistributedLockHandle>> TryAcquireInTransactionAsync(
        this IDbTransaction transaction,
        string resourceId,
        ILogger? logger = null,
        CancellationToken cancellationToken = default);

    // Same as above but accepts the concrete DbTransaction subtype (compile-time overload resolution)
    public static Task<Result<IDistributedLockHandle>> TryAcquireInTransactionAsync(
        this DbTransaction transaction,
        string resourceId,
        ILogger? logger = null,
        CancellationToken cancellationToken = default);

    // ── Blocking ────────────────────────────────────────────────────────────────
    // Retries internally until acquired or CancellationToken fires
    public static Task<Result<IDistributedLockHandle>> AcquireInTransactionAsync(
        this IDbTransaction transaction,
        string resourceId,
        ILogger? logger = null,
        CancellationToken cancellationToken = default);

    // Same as above for the concrete DbTransaction subtype
    public static Task<Result<IDistributedLockHandle>> AcquireInTransactionAsync(
        this DbTransaction transaction,
        string resourceId,
        ILogger? logger = null,
        CancellationToken cancellationToken = default);
}
```

### Overload Summary

| Method | `this` Type | Blocking? | Returns on Contention |
|---|---|:---:|---|
| `TryAcquireInTransactionAsync` | `IDbTransaction` | ❌ | `LockAlreadyHeld` |
| `TryAcquireInTransactionAsync` | `DbTransaction` | ❌ | `LockAlreadyHeld` |
| `AcquireInTransactionAsync` | `IDbTransaction` | ✅ | Only on cancellation |
| `AcquireInTransactionAsync` | `DbTransaction` | ✅ | Only on cancellation |

Prefer the `DbTransaction` overload when you hold a strongly-typed reference (e.g., from `await conn.BeginTransactionAsync()`). Use `IDbTransaction` for maximum interface compatibility (e.g., Dapper, EF Core interceptors).

---

## 4. Implementation-Internal Public Types

> [!NOTE]
> The following types are `public` for technical reasons (unit testability within the same assembly family) but are **implementation details** of the PostgreSQL provider. They should not be instantiated or referenced directly by consumer code. Always interact through `IDistributedLockHandle`.

### `PostgresAdvisoryLockHandle` (`EricksonLopez.DistributedLock.PostgreSql`)

Represents an active session-level advisory lock handle that:
- Holds a dedicated `DbConnection` for the lock lifetime
- Runs a `PeriodicTimer` keepalive heartbeat via `SELECT 1;`
- Cancels `HandleLostToken` on heartbeat failure
- Releases the lock via `pg_advisory_unlock` on `DisposeAsync()`

**Not intended for direct consumer instantiation.** The provider creates and returns this type via `TryAcquireAsync` / `AcquireAsync`, exposed to consumers as `IDistributedLockHandle` or `IAsyncDisposable`.

### `NoOpAsyncDisposable` (`EricksonLopez.DistributedLock.PostgreSql`)

Represents a no-operation lock handle returned for **transaction-bound locks** (`pg_advisory_xact_lock`). Transaction locks are released automatically by PostgreSQL on `COMMIT` or `ROLLBACK`, so no explicit `DisposeAsync()` action is needed.

Key characteristics:
- `HandleLostToken` is always `CancellationToken.None` (no heartbeat for transaction locks)
- `DisposeAsync()` returns `ValueTask.CompletedTask` (zero-allocation, no-op)
- `LockId` reflects the 64-bit hash computed from the resource identifier

**Not intended for direct consumer instantiation.**
