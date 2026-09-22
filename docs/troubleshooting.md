# Troubleshooting & Diagnostic Runbook — EricksonLopez.DistributedLock

> **Operational Diagnosis, Incident Remediation, and Root-Cause Analysis for Distributed Systems**  
> **Version**: 1.0.0 | **Ecosystem**: `EricksonLopez.*` | **Target Runtimes**: .NET 8.0, .NET 9.0, .NET 10.0

---

## 1. Frequent `DistributedLock.AlreadyHeld` on Sequential Operations

### Symptom
Services frequently encounter `DistributedLockErrors.LockAlreadyHeld` when processing operations that were intended to execute in sequence across worker replicas.

### Root Cause
The application is invoking immediate non-blocking `TryAcquireAsync(key)` instead of providing a timeout duration. Under normal distributed conditions, an operation finishing a few milliseconds later causes concurrent requests to be rejected immediately.

### Remediation
Switch to bounded waiting with backoff:
```csharp
// ❌ Rejects immediately if held:
var result = await provider.TryAcquireAsync("orders:101", ct);

// ✅ Waits up to 5 seconds with exponential jittered retries:
var result = await provider.TryAcquireAsync("orders:101", TimeSpan.FromSeconds(5), ct);
```

---

## 2. High `DistributedLock.Timeout` Rates Under Heavy Traffic

### Symptom
Under peak traffic spikes, clients report high rates of `DistributedLockErrors.Timeout`.

### Root Cause
1. **Oversized Critical Section**: The code executing within the lock performs slow operations (such as downstream HTTP REST calls, heavy serialization, or bulk database inserts).
2. **Thundering Herd Storm**: Multiple workers are polling on a fixed frequency without jitter, starving the database CPU or saturating connection pools.
3. **Underprovisioned Connection Pool**: The database connection pool (`Max Pool Size`) is exhausted by waiting lock connections.

### Remediation
1. **Shrink the Critical Section**: Move read operations and HTTP requests outside the lock boundary.
2. **Verify Jitter**: Ensure `BackoffJitter` is enabled (default in all dialect options).
3. **Inspect Metrics**: Examine OpenTelemetry metrics in Prometheus/Grafana:
   - `distributed_lock.wait_duration`: Identifies how long threads spend queued for locks.
   - `distributed_lock.hold_duration`: Highlights operations retaining locks excessively long.

---

## 3. `InvalidOperationException: The transaction connection is null or closed`

### Symptom
Calling `TryAcquireInTransactionAsync` throws an `InvalidOperationException`.

### Root Cause
The `IDbTransaction` or `DbTransaction` instance supplied to the method was invoked on a connection that was either already closed, disposed, or not yet opened.

### Remediation
Ensure the connection is opened and the transaction remains active until after the lock is acquired:
```csharp
await connection.OpenAsync(ct);
await using var tx = await connection.BeginTransactionAsync(ct);

// Must be acquired while tx.Connection is valid and open
var result = await tx.TryAcquireInTransactionAsync("key", logger, ct);
```

---

## 4. Locks Not Released After Catastrophic Container Termination (SIGKILL / OOM)

### Symptom
A worker pod crashes abruptly (e.g., Kubernetes OOMKilled or `SIGKILL`), and other pods report the lock remains locked.

### Diagnostic Analysis by Engine
- **PostgreSQL / SQL Server / MySQL / MariaDB**:
  - Session-level locks are tied to the server-side TCP socket connection.
  - When a container dies abruptly, the operating system kernel usually sends a TCP `FIN` or `RST` packet, causing the database server to close the backend connection and release the lock immediately.
  - **Issue**: If a stateful network firewall or NAT gateway drops the connection silently without forwarding `RST`, the database server considers the connection alive until TCP keepalives expire (which can take minutes by default).
  - **Remediation**: Configure `KeepaliveCadence` in lock options (e.g. 5–15 seconds) and adjust OS TCP keepalive settings (`tcp_keepalives_idle`, `tcp_keepalives_interval`).
- **SQLite**:
  - SQLite locks are stored in the `__distributed_locks` table.
  - If a process dies before removing its row, the lock automatically becomes reclaimable once its `LockTtl` (default 60 seconds) elapses.
- **Redis**:
  - Redis locks expire automatically when `DefaultExpiry` (default 30 seconds) elapses.

---

## 5. Lock Loss During Background Execution (`HandleLostToken` Triggered)

### Symptom
A background task aborts with `OperationCanceledException` even though the user-provided `CancellationToken` was not triggered.

### Root Cause
The task was linked with `IDistributedLockHandle.HandleLostToken`, and the underlying database connection was dropped, severed by a network partition, or failed over to a replica.

### Remediation
This is the intended fail-safe behavior. Catch `OperationCanceledException`, verify if `handle.HandleLostToken.IsCancellationRequested` is `true`, and log an incident indicating the lock was severed before retrying the workflow from the beginning.
