# ADR-003: Session Lock Keepalive and Handle Lost Token

## Status
Accepted

## Date
2026-09-03

**Context**: Distributed Lock Safety and Network Resilience  
**Decision Makers**: Principal Software Architect, Core Infrastructure Team  

---

[← Previous: ADR-002](adr-002-distributed-lock-options-and-di-registration.md) | [Index](README.md) | [Next: ADR-004 →](adr-004-opentelemetry-metrics-and-observability.md)

---

## 1. Context and Problem Statement

In PostgreSQL, session-level advisory locks (`pg_advisory_lock` and `pg_try_advisory_lock`) are bound to the underlying TCP connection established between the client and the database server. If that physical connection terminates abruptly (e.g., intermediate network switch failure, stateful firewall silent drop without TCP RST, database server failover, or PostgreSQL container restart), the database engine automatically releases the held advisory locks server-side to prevent perpetual deadlocks.

However, on the client .NET application side, worker code executing within the critical section block:
```csharp
await using (lockHandle)
{
    // Executing prolonged critical operation (e.g., 5 minutes)
}
```
receives no immediate notification that the underlying physical connection has severed unless it attempts to issue a query across that specific socket. As a result, **another replica in the cluster can acquire the now-released lock in PostgreSQL**, causing concurrent execution of the critical section and permanently violating cluster-wide mutual exclusion guarantees.

---

## 2. Decision

We introduce an active **Keepalive Heartbeat** mechanism alongside a cooperative cancellation token **`HandleLostToken`**:

1. **`IDistributedLockHandle` Abstraction**:
   We extend `IAsyncDisposable` with the interface:
   ```csharp
   public interface IDistributedLockHandle : IAsyncDisposable
   {
       CancellationToken HandleLostToken { get; }
       string ResourceId { get; }
       long LockId { get; }
   }
   ```
2. **Periodic Keepalive Cycle (`PeriodicTimer`)**:
   - `PostgresAdvisoryLockHandle` spawns a background worker task governed by a `PeriodicTimer` with a configurable cadence (`PostgresLockOptions.KeepaliveCadence`, default 30 seconds).
   - On each timer tick, a lightweight `SELECT 1;` ping is emitted across the dedicated connection socket.
   - Execution utilizes pure ADO.NET commands without reflection, strictly adhering to Native AOT directives.
3. **Lock Loss Signaling**:
   - If the connection state transitions away from `ConnectionState.Open` or if executing `SELECT 1;` throws an I/O or network exception, the handle immediately deduces that the PostgreSQL session has terminated and the lock has been released server-side.
   - The handle immediately cancels its internal `CancellationTokenSource` (`HandleLostToken`).
   - It increments the OpenTelemetry metric instrument `distributed_lock.lost`.
4. **Cooperative Consumption Pattern**:
   Long-running consumers link their operation cancellation token with `HandleLostToken`:
   ```csharp
   await using var handle = lockResult.Value;
   using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
       cancellationToken, 
       handle.HandleLostToken);

   await LongRunningOperationAsync(linkedCts.Token);
   ```

---

## 3. Consequences

### Positive
- **Split-Brain Mitigation on Network Drops**: The consumer aborts execution cooperatively if the database connection terminates, preventing shared state corruption or duplicate execution.
- **Active Socket Heartbeat**: Prevents intermediate stateful firewalls, proxies, or cloud load balancers from pruning idle TCP connections during prolonged computation within the critical section.
- **Zero Overhead for Short-Lived Operations**: Standard `TryAcquireAsync` returning generic `IAsyncDisposable` remains available for rapid, atomic tasks without background heartbeat overhead.

### Negative and Mitigations
- **Additional Network Overhead**: Each active session lock emits a `SELECT 1;` ping every 30 seconds.
  *Mitigation*: `SELECT 1;` transmits under 100 bytes and consumes negligible microsecond database resources. The cadence can be tuned or disabled via `PostgresLockOptions.KeepaliveCadence = TimeSpan.Zero` if not required.

---

[← Previous: ADR-002](adr-002-distributed-lock-options-and-di-registration.md) | [Index](README.md) | [Next: ADR-004 →](adr-004-opentelemetry-metrics-and-observability.md)
