# Best Practices & Operational Guidelines — EricksonLopez.DistributedLock

> **Engineering Recommendations, Operational Patterns, and Resilience Practices for High-Availability Distributed Deployments**  
> **Version**: 1.0.0 | **Ecosystem**: `EricksonLopez.*` | **Target Runtimes**: .NET 8.0, .NET 9.0, .NET 10.0

---

## 1. Connection Pooling & Proxy Invariants (PgBouncer & ProxySQL)

### The Underlying Mechanics
In PostgreSQL, session-level advisory locks (`pg_advisory_lock`) are bound directly to the physical server process identifier (`backend_pid`) of the connection on the PostgreSQL host.

### The Connection Pooler Hazard
When utilizing external connection poolers (such as **PgBouncer** or **ProxySQL**) configured with `pool_mode = transaction` or `pool_mode = statement`, individual queries issued through the same logical .NET client connection can be multiplexed across differing physical server processes. This results in:
- Silent lock loss or lock orphaning.
- Accidental release failures because another physical process cannot release an advisory lock acquired by a different `backend_pid`.
- Cross-connection contention and pool starvation.

### Operational Remedies
1. **For Transactional Workflows**: Always use transaction-bound locks (`TryAcquireInTransactionAsync` / `AcquireInTransactionAsync`). Transaction locks bind to the ambient transaction and automatically release upon `COMMIT` or `ROLLBACK`, guaranteeing complete compatibility with PgBouncer transaction-mode pools.
2. **For Session-Scoped Background Jobs**: Point the lock provider's connection factory (`Func<DbConnection>`) directly to a dedicated PgBouncer pool configured with `pool_mode = session` or bypass the pooler directly to the primary PostgreSQL server.

---

## 2. Keep Critical Sections Minimal

Distributed locks are designed to coordinate atomic state transitions across a cluster, **not** to serialize long-running I/O or multi-second batch calculations.

### Anti-Pattern: Holding Locks Across Slow External I/O
```csharp
// ❌ ANTI-PATTERN: Holding distributed lock across slow external HTTP calls
await using (var handle = (await provider.AcquireAsync("order:101")).Value)
{
    var paymentResponse = await httpClient.PostAsync("https://external-gateway.com/pay", ...); // 1500ms delay!
    await UpdateOrderDatabaseAsync(paymentResponse);
}
```

### Best Practice: Narrowed Critical Section
Fetch inputs, execute non-mutating validation, or perform external I/O *outside* the lock. Acquire the lock only for the final mutation and persistence:
```csharp
// ✅ BEST PRACTICE: Isolate the lock strictly around the state mutation
var preAuthToken = await httpClient.PostAsync("https://external-gateway.com/preauth", ...);

await using (var handle = (await provider.AcquireAsync("order:101")).Value)
{
    await UpdateOrderDatabaseAsync(preAuthToken);
}
```

---

## 3. Always Link `HandleLostToken` for Extended Operations

If a critical section must execute for more than a few seconds (e.g. multi-step data migration or batch reconciliation), always link your task's cancellation token with `handle.HandleLostToken`:

```csharp
var result = await provider.TryAcquireHandleAsync("heavy:sync:tenant-1");
if (result.IsSuccess)
{
    await using var handle = result.Value;
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
        cancellationToken,
        handle.HandleLostToken);

    while (!linkedCts.Token.IsCancellationRequested)
    {
        await ProcessNextBatchItemAsync(linkedCts.Token);
    }
}
```

If a network cable is unplugged or the database fails over, the background keepalive heartbeat will immediately trigger `HandleLostToken`, preventing zombie workers from executing unauthorized writes.

---

## 4. Retain Backoff Jitter to Eliminate Thundering Herds

Fixed-interval polling creates synchronization waves where dozens of competing container replicas barrage the database simultaneously the instant a lock is released.

- Keep `BackoffJitter = true` (or `JitterRatio` between `0.2` and `0.5` in PostgreSQL).
- Jitter introduces randomized variance into every retry interval, spreading acquisition attempts evenly across the timeline and reducing database CPU spikes during high contention.

---

## 5. Validate Fencing Tokens on Storage Writes (Kleppmann Pattern)

Even with keepalives, a long Garbage Collection pause (Stop-The-World GC) or VM freeze can cause a worker to be unresponsive while its lease expires server-side. Another worker claims the lock and begins writing. When the frozen worker awakens, it may write stale data.

To protect against this hazard, utilize monotonic fencing tokens:
```sql
UPDATE inventory
SET stock_count = stock_count - @Quantity,
    last_fencing_token = @FencingToken
WHERE sku = @Sku
  AND (last_fencing_token IS NULL OR last_fencing_token < @FencingToken);
```

If the rows affected is `0`, the application detects that another replica took over during the stall, safely rejecting the stale write.

---

## 6. Resource Key Naming Conventions

Maintain strict, structured naming conventions for lock keys:
- Use colon-delimited hierarchical namespaces: `domain:entity:operation:identifier`
  - Example: `orders:checkout:user-98124`
  - Example: `billing:monthly-run:tenant-42`
  - Example: `inventory:reconciliation:warehouse-east`
- Keep keys concise to optimize hashing speed and memory footprint.
- Avoid variable runtime data in key names when monitoring OpenTelemetry metrics to keep tag cardinality bounded.

---

## 7. Zero-Allocation Error Handling (Avoid Exception Control Flow)

Never throw or catch exceptions for expected concurrency states:
```csharp
// ✅ BEST PRACTICE: Inspect strongly typed Result
var result = await provider.TryAcquireAsync("resource:key");
if (result.IsFailure)
{
    if (result.Error == DistributedLockErrors.LockAlreadyHeld)
    {
        // Normal, expected contention state
        return;
    }
}
```

The Railway-Oriented Programming model (`Result<T>`) completely avoids thread suspension, stack unwinding, and GC pressure during heavy contention.
