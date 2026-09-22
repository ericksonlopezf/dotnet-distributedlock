# Distributed Locking Cookbook — EricksonLopez.DistributedLock

> **12 Production-Ready Recipes for High-Throughput Distributed Coordination in .NET**  
> **Version**: 1.0.0 | **Ecosystem**: `EricksonLopez.*` | **Target Runtimes**: .NET 8.0, .NET 9.0, .NET 10.0

---

## Recipe Index

1. [Recipe 1: Non-Blocking Immediate Attempt](#recipe-1-non-blocking-immediate-attempt)
2. [Recipe 2: Bounded Waiting with Jittered Exponential Backoff](#recipe-2-bounded-waiting-with-jittered-exponential-backoff)
3. [Recipe 3: Declarative Scope Guard with Automatic Token Linking](#recipe-3-declarative-scope-guard-with-automatic-token-linking)
4. [Recipe 4: Database Transaction-Bound Advisory Locks](#recipe-4-database-transaction-bound-advisory-locks)
5. [Recipe 5: Long-Running Worker Socket Severance Detection](#recipe-5-long-running-worker-socket-severance-detection)
6. [Recipe 6: Monotonic Fencing Token Validation for Storage Writes](#recipe-6-monotonic-fencing-token-validation-for-storage-writes)
7. [Recipe 7: OpenTelemetry Metrics Instrumentation & Monitoring](#recipe-7-opentelemetry-metrics-instrumentation--monitoring)
8. [Recipe 8: Multi-Engine Dialect Switching via Environment Configuration](#recipe-8-multi-engine-dialect-switching-via-environment-configuration)
9. [Recipe 9: Declarative Attribute Processing in CQRS / MediatR Pipelines](#recipe-9-declarative-attribute-processing-in-cqrs--mediatr-pipelines)
10. [Recipe 10: Graceful Host Shutdown & CancellationToken Propagation](#recipe-10-graceful-host-shutdown--cancellationtoken-propagation)
11. [Recipe 11: Blocking Acquire Overloads](#recipe-11-blocking-acquire-overloads)
12. [Recipe 12: Transaction Lock Blocking & DbTransaction Overloads](#recipe-12-transaction-lock-blocking--dbtransaction-overloads)

---

### Recipe 1: Non-Blocking Immediate Attempt

#### Problem
You need to attempt lock acquisition for a periodic maintenance job. If another replica is already processing the task, this worker should skip execution immediately without blocking threads or waiting.

#### Solution: `provider.TryAcquireAsync(key)`
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;

public async Task RunBackgroundMaintenanceAsync(
    IDistributedLockProvider provider,
    CancellationToken ct)
{
    var lockResult = await provider.TryAcquireAsync("jobs:cleanup:audit-logs", ct);
    if (lockResult.IsFailure)
    {
        // Concurrently held by another worker replica — skip execution gracefully
        return;
    }

    await using (lockResult.Value)
    {
        await PurgeExpiredAuditRecordsAsync(ct);
    } // Handle disposed, releasing the lock back to the database
}
```

---

### Recipe 2: Bounded Waiting with Jittered Exponential Backoff

#### Problem
A checkout operation can tolerate waiting up to 5 seconds if a previous request is finishing, but must fail deterministically with a typed timeout error if that duration is exceeded.

#### Solution: `provider.TryAcquireAsync(key, timeout, cancellationToken)`
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.Result;

public async Task<Result<OrderReceipt>> CheckoutAsync(
    IDistributedLockProvider provider,
    string customerId,
    CancellationToken ct)
{
    var timeout = TimeSpan.FromSeconds(5);
    var lockResult = await provider.TryAcquireAsync($"checkout:{customerId}", timeout, ct);

    if (lockResult.IsFailure)
    {
        if (lockResult.Error == DistributedLockErrors.Timeout)
        {
            return Result<OrderReceipt>.Failure(
                new Error("Checkout.Busy", "Another order is being processed for this account. Please try again."));
        }

        return Result<OrderReceipt>.Failure(lockResult.Error);
    }

    await using (lockResult.Value)
    {
        return await ProcessOrderPaymentAsync(customerId, ct);
    }
}
```

---

### Recipe 3: Declarative Scope Guard with Automatic Token Linking

#### Problem
You want to execute a critical section without manually managing `await using` blocks or forgetting to link the cancellation token with unexpected socket drop detection.

#### Solution: `provider.ExecuteWithLockAsync<T>(key, action, ct)`
```csharp
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.Result;

public async Task<Result<InventoryAllocation>> AllocateStockAsync(
    IDistributedLockProvider provider,
    string sku,
    int quantity,
    CancellationToken ct)
{
    return await provider.ExecuteWithLockAsync(
        $"inventory:sku:{sku}",
        async (linkedToken) =>
        {
            // linkedToken fires if caller cancels OR if backend connection is severed
            return await DecrementWarehouseStockAsync(sku, quantity, linkedToken);
        },
        ct);
}
```

---

### Recipe 4: Database Transaction-Bound Advisory Locks

#### Problem
When running behind PgBouncer or ProxySQL in `pool_mode = transaction`, session locks can leak across physical connections. You require a lock that strictly binds to an existing `DbTransaction` and releases automatically upon commit or rollback.

#### Solution: `dbTransaction.TryAcquireInTransactionAsync(key, logger)`
```csharp
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.PostgreSql;
using Microsoft.Extensions.Logging;

public async Task<bool> ExecuteTransactionalRebalanceAsync(
    DbConnection connection,
    ILogger logger,
    CancellationToken ct)
{
    await using var tx = await connection.BeginTransactionAsync(ct);

    // Acquires pg_try_advisory_xact_lock on the ambient transaction
    var lockResult = await tx.TryAcquireInTransactionAsync("rebalance:ledger:global", logger, ct);
    if (lockResult.IsFailure)
    {
        await tx.RollbackAsync(ct);
        return false;
    }

    await ExecuteLedgerPostingsAsync(tx, ct);

    // Lock is automatically released on commit or rollback by PostgreSQL
    await tx.CommitAsync(ct);
    return true;
}
```

---

### Recipe 5: Long-Running Worker Socket Severance Detection

#### Problem
A worker executing a multi-minute synchronization process must abort immediately if its database TCP connection drops or a backend failover occurs, preventing corrupted split-brain writes.

#### Solution: `handle.HandleLostToken`
```csharp
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;

public async Task ProcessMassiveSyncJobAsync(
    IDistributedLockProvider provider,
    CancellationToken stoppingToken)
{
    var handleResult = await provider.TryAcquireHandleAsync("jobs:sync:catalog", stoppingToken);
    if (handleResult.IsFailure)
    {
        return;
    }

    await using var handle = handleResult.Value;

    // Link caller cancellation token with the proactive keepalive loss detector
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
        stoppingToken,
        handle.HandleLostToken);

    while (!linkedCts.Token.IsCancellationRequested)
    {
        var batch = await FetchNextBatchAsync(linkedCts.Token);
        if (batch.IsEmpty) break;

        await ProcessBatchAsync(batch, linkedCts.Token);
    }
}
```

---

### Recipe 6: Monotonic Fencing Token Validation for Storage Writes

#### Problem
Guard against Stop-The-World (STW) Garbage Collection pauses where a worker stalls beyond the lock lease, another node takes ownership, and the stalled worker resumes and writes stale data.

#### Solution: `handle.FencingToken`
```csharp
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using EricksonLopez.DistributedLock.Abstractions;

public async Task UpdateDocumentWithFencingAsync(
    IDistributedLockProvider provider,
    string documentId,
    string updatedContent,
    DbConnection dbConnection,
    CancellationToken ct)
{
    var handleResult = await provider.TryAcquireHandleAsync($"docs:{documentId}", ct);
    if (handleResult.IsFailure)
    {
        return;
    }

    await using var handle = handleResult.Value;
    var fencingToken = handle.FencingToken ?? DateTime.UtcNow.Ticks;

    const string sql = @"
        UPDATE documents
        SET content = @Content, last_fencing_token = @Token
        WHERE id = @Id AND (last_fencing_token IS NULL OR last_fencing_token < @Token);";

    var rows = await dbConnection.ExecuteAsync(sql, new
    {
        Content = updatedContent,
        Token = fencingToken,
        Id = documentId
    });

    if (rows == 0)
    {
        throw new InvalidOperationException("Optimistic concurrency violation: Stale write rejected by fencing token.");
    }
}
```

---

### Recipe 7: OpenTelemetry Metrics Instrumentation & Monitoring

#### Problem
Monitor lock contention, acquisition latencies, and unexpected loss incidents across your microservice cluster in Grafana / Prometheus.

#### Solution: Subscribe to `DistributedLockMetrics.MeterName`
```csharp
using EricksonLopez.DistributedLock.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;

builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics
            .AddMeter(DistributedLockMetrics.MeterName)
            .AddPrometheusExporter();
    });
```

---

### Recipe 8: Multi-Engine Dialect Switching via Environment Configuration

#### Problem
Use lightweight SQLite for local developer workstations and unit testing, while dynamically activating PostgreSQL or SQL Server in staging and production environments.

#### Solution: Environment-conditional DI registration
```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

public static void ConfigureLockStorage(IServiceCollection services, IHostEnvironment env, IConfiguration config)
{
    if (env.IsDevelopment())
    {
        // Local SQLite file coordination
        services.AddSqliteDistributedLock(config.GetConnectionString("SqliteLock") ?? "Data Source=local_locks.db;");
    }
    else
    {
        // Production PostgreSQL advisory locks
        services.AddPostgresDistributedLock(
            sp => new Npgsql.NpgsqlConnection(config.GetConnectionString("PostgreSql")!));
    }
}
```

---

### Recipe 9: Declarative Attribute Processing in CQRS / MediatR Pipelines

#### Problem
Enforce serialized processing on aggregate roots without polluting command handlers with explicit lock acquisition code.

#### Solution: `[DistributedLock]` attribute with MediatR pipeline behavior
```csharp
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using MediatR;

// 1. Tag the command declaratively
[DistributedLock("orders:{OrderId}", TimeoutSeconds = 5)]
public record ProcessOrderCommand(Guid OrderId, decimal Total) : IRequest<bool>;

// 2. IPipelineBehavior automatically inspects metadata
public sealed class DistributedLockBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IDistributedLockProvider _lockProvider;

    public DistributedLockBehavior(IDistributedLockProvider lockProvider)
    {
        _lockProvider = lockProvider;
    }

    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var lockAttr = typeof(TRequest).GetCustomAttribute<DistributedLockAttribute>();
        if (lockAttr is null)
        {
            return await next();
        }

        // Resolve dynamic key pattern (e.g., replace {OrderId})
        var resourceKey = ResolveKey(lockAttr.ResourceKeyPattern, request);
        var timeout = TimeSpan.FromSeconds(lockAttr.TimeoutSeconds);

        var result = await _lockProvider.TryAcquireAsync(resourceKey, timeout, cancellationToken);
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Could not acquire lock for {resourceKey}: {result.Error.Code}");
        }

        await using (result.Value)
        {
            return await next();
        }
    }

    private static string ResolveKey(string pattern, TRequest request) =>
        pattern.Replace("{OrderId}", (request as ProcessOrderCommand)?.OrderId.ToString() ?? "default");
}
```

---

### Recipe 10: Graceful Host Shutdown & CancellationToken Propagation

#### Problem
When a Kubernetes pod receives `SIGTERM`, active lock acquisition loops must terminate immediately, and held locks must be cleanly freed before container shutdown.

#### Solution: Pass `IHostApplicationLifetime.ApplicationStopping` to all lock calls
```csharp
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using Microsoft.Extensions.Hosting;

public sealed class CleanWorker : BackgroundService
{
    private readonly IDistributedLockProvider _lockProvider;

    public CleanWorker(IDistributedLockProvider lockProvider)
    {
        _lockProvider = lockProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var result = await _lockProvider.TryAcquireAsync("cron:hourly-reconciliation", stoppingToken);
            if (result.IsFailure)
            {
                if (result.Error == DistributedLockErrors.Canceled)
                {
                    // Host shutdown requested during acquisition — exit cleanly
                    break;
                }

                await Task.Delay(5000, stoppingToken);
                continue;
            }

            await using (result.Value)
            {
                await ExecuteHourlyWorkAsync(stoppingToken);
            }
        }
    }

    private Task ExecuteHourlyWorkAsync(CancellationToken ct) => Task.CompletedTask;
}
```

---

### Recipe 11: Blocking Acquire Overloads

#### Problem
You need the acquisition to wait indefinitely (or up to a maximum time) until the lock becomes available — unlike `TryAcquireAsync`, which returns immediately on contention. You also need the `IDistributedLockHandle` variant to access `HandleLostToken` and `FencingToken` in long-running workers.

#### Solution: `AcquireAsync` / `AcquireHandleAsync` / `ExecuteWithLockAsync(timeout, action)`

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;

// 1. AcquireAsync(string, CancellationToken) — blocks indefinitely until acquired
var acquireResult = await provider.AcquireAsync("jobs:monthly-report:run", ct);
if (acquireResult.IsSuccess)
{
    await using (acquireResult.Value)
    {
        await ExecuteMonthlyReportAsync(ct);
    } // Lock released on exit
}

// 2. AcquireAsync(string, TimeSpan, CancellationToken) — blocks up to the timeout
var timedResult = await provider.AcquireAsync("jobs:monthly-report:run", TimeSpan.FromSeconds(10), ct);
if (timedResult.IsFailure && timedResult.Error == DistributedLockErrors.Timeout)
{
    // Another worker held the lock for longer than 10 seconds
    return;
}

// 3. AcquireHandleAsync — returns IDistributedLockHandle with HandleLostToken
var handleResult = await provider.AcquireHandleAsync("jobs:long-sync", ct);
if (handleResult.IsSuccess)
{
    await using var handle = handleResult.Value;
    // handle.HandleLostToken fires on TCP drop / failover
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, handle.HandleLostToken);
    await DoLongRunningWorkAsync(linked.Token);
}

// 4. AcquireHandleAsync(string, TimeSpan, CancellationToken) — handle with bounded wait
var timedHandleResult = await provider.AcquireHandleAsync("resource:exclusive", TimeSpan.FromSeconds(5), ct);

// 5. ExecuteWithLockAsync(string, TimeSpan, Func<ct, Task>) — scope guard with bounded acquisition
var scopeResult = await provider.ExecuteWithLockAsync(
    "report:generate",
    TimeSpan.FromSeconds(30),
    async (linkedToken) =>
    {
        await GenerateReportAsync(linkedToken);
    },
    ct);
```

#### Explanation

| Overload | Behaviour on Contention |
|---|---|
| `TryAcquireAsync(key)` | Returns immediately with `LockAlreadyHeld` |
| `TryAcquireAsync(key, timeout)` | Retries with jitter until timeout, then `Timeout` |
| `AcquireAsync(key, ct)` | Blocks indefinitely; only fails on cancellation |
| `AcquireAsync(key, timeout, ct)` | Blocks up to timeout; returns `Timeout` on expiry |
| `AcquireHandleAsync(key, ct)` | Same as `AcquireAsync` but returns `IDistributedLockHandle` |
| `AcquireHandleAsync(key, timeout, ct)` | Bounded handle acquisition with expiry |
| `ExecuteWithLockAsync(key, timeout, action)` | Scope guard that uses bounded acquisition internally |

#### Best Practices
- Use `AcquireAsync` only when missing the lock means losing work (e.g., singleton scheduled jobs).
- Always supply a `CancellationToken` to guarantee the call is bounded by host shutdown.
- Prefer the `IDistributedLockHandle` variant when you need `HandleLostToken` for long-running workers.

#### Common Errors
- Calling `AcquireAsync` without a cancellation token in a high-contention system causes unbounded waits under deadlock conditions.
- Confusing `Timeout` error (acquisition deadline exceeded) with `Canceled` (token was signalled externally).

---

### Recipe 12: Transaction Lock Blocking & DbTransaction Overloads

#### Problem
When working with the `SqliteTransactionLockExtensions` (or equivalent dialect extension), you need to demonstrate: (a) the `DbTransaction` overload (strongly-typed subclass, not `IDbTransaction`), and (b) the blocking `AcquireInTransactionAsync` variant that retries until the transactional lock is available.

#### Solution: `SqliteTransactionLockExtensions` — all four overloads

```csharp
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

// ── Overload 1: TryAcquireInTransactionAsync(IDbTransaction) ────────────────
await using var conn1 = new SqliteConnection("Data Source=myapp.db;");
await conn1.OpenAsync();
await using var tx1 = await conn1.BeginTransactionAsync();

var tryResult = await ((IDbTransaction)tx1).TryAcquireInTransactionAsync(
    "ledger:close:day", NullLogger.Instance);
if (tryResult.IsSuccess)
{
    await using (tryResult.Value)
    {
        await ExecuteLedgerCloseAsync();
    }
    await tx1.CommitAsync(); // Lock auto-released on commit
}

// ── Overload 2: TryAcquireInTransactionAsync(DbTransaction) ─────────────────
// Strongly-typed variant — resolved at compile time via static type
await using var conn2 = new SqliteConnection("Data Source=myapp.db;");
await conn2.OpenAsync();
await using var tx2 = (DbTransaction)(await conn2.BeginTransactionAsync());

var tryDbResult = await tx2.TryAcquireInTransactionAsync(
    "ledger:close:day-2", NullLogger.Instance);
if (tryDbResult.IsSuccess)
{
    await using (tryDbResult.Value)
    {
        // Access FencingToken and ResourceId through the handle
        Console.WriteLine($"FencingToken: {tryDbResult.Value.FencingToken}");
    }
    await tx2.CommitAsync();
}

// ── Overload 3: AcquireInTransactionAsync(IDbTransaction) ───────────────────
// Blocking: retries internally until the transactional lock is acquired
await using var conn3 = new SqliteConnection("Data Source=myapp.db;");
await conn3.OpenAsync();
await using var tx3 = await conn3.BeginTransactionAsync();

var acquireResult = await ((IDbTransaction)tx3).AcquireInTransactionAsync(
    "ledger:close:day-3", NullLogger.Instance);
if (acquireResult.IsSuccess)
{
    await using (acquireResult.Value)
    {
        await ExecuteAtomicPostingsAsync();
    }
    await tx3.CommitAsync();
}

// ── Overload 4: AcquireInTransactionAsync(DbTransaction) ────────────────────
await using var conn4 = new SqliteConnection("Data Source=myapp.db;");
await conn4.OpenAsync();
await using var tx4 = (DbTransaction)(await conn4.BeginTransactionAsync());

var acquireDbResult = await tx4.AcquireInTransactionAsync(
    "ledger:close:day-4", NullLogger.Instance);
if (acquireDbResult.IsSuccess)
{
    await using (acquireDbResult.Value)
    {
        await ExecuteAtomicPostingsAsync();
    }
    await tx4.CommitAsync();
}
```

#### Explanation

| Overload | Type parameter | Blocking |
|---|---|:---:|
| `TryAcquireInTransactionAsync(IDbTransaction, key, logger)` | Interface — broadest compatibility | ❌ |
| `TryAcquireInTransactionAsync(DbTransaction, key, logger)` | Concrete subclass — compile-time resolution | ❌ |
| `AcquireInTransactionAsync(IDbTransaction, key, logger)` | Interface | ✅ |
| `AcquireInTransactionAsync(DbTransaction, key, logger)` | Concrete subclass | ✅ |

#### Best Practices
- Prefer `DbTransaction` overloads when you already hold a strongly typed reference (e.g., from `BeginTransactionAsync()`).
- Use `AcquireInTransactionAsync` when the lock must be obtained before proceeding (e.g., exclusive ledger operations). Use `TryAcquireInTransactionAsync` when you can gracefully skip the critical section.
- Transactional locks automatically release when the transaction commits or rolls back — no explicit `DisposeAsync` on the lock is required beyond the `await using` scope.

#### Common Errors
- Calling `AcquireInTransactionAsync` without a timeout while another long transaction holds the same lock key causes unbounded waits; ensure all transactions have a command timeout configured.
- Using `IDbTransaction` when the concrete type is `DbTransaction` can prevent the correct extension method overload from being resolved.
