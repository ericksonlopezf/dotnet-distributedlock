# ADR-001: Distributed Lock Abstraction

## Status
Accepted

## Date
2026-08-30 (Revision: 2026-09-03)

**Context**: Concurrency and Resilience across the Ecosystem  
**Decision Makers**: Principal Software Architect, Core Infrastructure Team  
**Global Traceability**: Cataloged as **ADR-015** in the EricksonLopez ecosystem macroarchitecture.

---

[Index](README.md) | [Next: ADR-002 →](adr-002-distributed-lock-options-and-di-registration.md)

---

## 1. Context and Problem Statement

In distributed multi-replica deployments (such as concurrent application instances, Kubernetes microservices, or independent background workers), concurrent execution and shared-state mutations create race conditions and duplicated critical work.

Critical production examples include:
1. **Recurring Invoicing**: Multiple nodes trigger execution at midnight, generating duplicate customer invoices.
2. **Materialized View Refreshes and Fiscal Syncs**: Multiple nodes overwhelm the database competing for the same batch job.
3. **Irreversible External Side-Effects**: Dispatching transactional emails, banking authorization holds, and non-idempotent third-party API calls.

While optimistic concurrency control (OCC) is handled natively via `EricksonLopez.Concurrency`, long-running workflows and external side-effects require **Cluster-Level Pessimistic Concurrency Control (PCC)**.

Standard industry solutions typically mandate deploying and operating dedicated Redis (Redlock), ZooKeeper, or Consul clusters, introducing infrastructure overhead, additional monitoring, and subtle split-brain failure modes in baseline enterprise environments.

---

## 2. Decision

We establish **EricksonLopez.DistributedLock** as a Tier 0 infrastructure abstraction guided by the following architectural invariants:

### 2.1. Decoupled Contract and Railway-Oriented Programming
- The abstraction resides in `EricksonLopez.DistributedLock.Abstractions` under the `IDistributedLockProvider` interface.
- Lock acquisition **never throws exceptions** on contention or timeouts. Instead, it returns an `EricksonLopez.Result` struct `Result<IAsyncDisposable>` or `Result<IDistributedLockHandle>` with structured error codes (`DistributedLockErrors.LockAlreadyHeld`, `DistributedLockErrors.Timeout`, `DistributedLockErrors.LockLost`, `DistributedLockErrors.Canceled`).
- Acquired handles implement `IAsyncDisposable`, ensuring deterministic release via native C# `await using` syntax.

### 2.2. Native PostgreSQL Primitives
We implement `EricksonLopez.DistributedLock.PostgreSql` utilizing **PostgreSQL Advisory Locks**:
- **Session-Level Locks** (`pg_try_advisory_lock` / `pg_advisory_lock`): Held at the session level over a dedicated physical connection provided by a connection factory (`Func<DbConnection>`). Explicitly released via `pg_advisory_unlock` upon `DisposeAsync()`, or automatically released by PostgreSQL if the underlying TCP connection terminates.
- **Transaction-Level Locks** (`pg_try_advisory_xact_lock` / `pg_advisory_xact_lock`): Bound directly to the lifecycle of an active PostgreSQL transaction (`IDbTransaction`). Automatically released by the database engine when the transaction executes `COMMIT` or `ROLLBACK`.

### 2.3. Acquisition Modalities: Blocking vs. Non-Blocking
1. **Non-Blocking (`TryAcquireAsync`)**: Immediately attempts to acquire the lock. If held by another process, it instantly returns `Result.Failure(DistributedLockErrors.LockAlreadyHeld)` without thread suspension.
2. **Polling with Timeout and Jitter (`TryAcquireAsync(timeout)`)**: Executes successive attempts applying exponential backoff with randomized jitter (`PostgresLockOptions.JitterRatio`) to mitigate thundering herd spikes when multiple replicas compete after resource release.
3. **Native Blocking (`AcquireAsync`)**: Delegates waiting directly to PostgreSQL via `SELECT pg_advisory_lock(@LockId);` (or `pg_advisory_xact_lock`), blocking server-side until the lock is granted or the `CancellationToken` is triggered.

### 2.4. Session Safety: Keepalive Heartbeat and `HandleLostToken`
Because session-level locks depend on persistent TCP connections, silent network drops (intermediate firewalls, database server restarts, TCP half-open states) could cause PostgreSQL to release the lock while the local worker continues executing unaware.
- To eliminate this hazard, `PostgresAdvisoryLockHandle` incorporates a periodic **Keepalive Heartbeat** using `PeriodicTimer` (`PostgresLockOptions.KeepaliveCadence`, default 30s) emitting `SELECT 1;` pings.
- If the ping fails or the connection drops, `HandleLostToken` (`CancellationToken`) is immediately cancelled, alerting the consumer to abort the critical section at once.

### 2.5. Mathematical Key Derivation (SHA-256 Hashing)
PostgreSQL Advisory Locks operate internally on 64-bit signed integers (`bigint`). To transform arbitrary textual resource identifiers into numeric keys:
- The UTF-8 string is hashed via `SHA-256`, and the first 8 bytes are extracted using `BinaryPrimitives.ReadInt64LittleEndian(hash)` (little-endian byte order) to produce a deterministic 64-bit signed integer.
- The accidental collision probability across a 64-bit keyspace with $n$ concurrent resources follows the birthday paradox:
  $$P(\text{collision}) \approx 1 - e^{-\frac{n^2}{2 \times 2^{64}}}$$
  For 100,000 distinct resources in the cluster, the collision probability is under $2.71 \times 10^{-10}$ (virtually zero in enterprise operations). Domain/tenant prefix namespacing (e.g. `"tenant:module:entity:id"`) is recommended.

### 2.6. Critical Re-entrancy Caveat
In PostgreSQL, advisory locks **are re-entrant at the session level**. If the same connection executes `pg_advisory_lock` twice with the same key, PostgreSQL grants both attempts and increments an internal counter. Consequently, releasing the lock **requires the exact same number of calls to `pg_advisory_unlock`**.
Our architecture eliminates this danger by allocating a dedicated, independent connection per instantiated handle.

### 2.7. Incompatibility with PgBouncer in Transaction Mode
> [!CAUTION]
> Session-level advisory locks (`pg_advisory_lock` / `pg_try_advisory_lock`) are **strictly incompatible with PgBouncer running in `pool_mode = transaction` or `pool_mode = statement`**. In those modes, successive queries route across different backend connections, causing orphaned locks or releasing locks in erroneous sessions.
> 
> **Operational Rule**:
> 1. In environments with PgBouncer in `pool_mode = transaction`, exclusively use transaction-level locks (`pg_try_advisory_xact_lock`) attached to active transactions.
> 2. Or configure the connection factory to connect directly to PostgreSQL without passing through PgBouncer's transaction pool.

---

## 3. Initial System Discards — Historical Record

> [!NOTE]
> **Architectural Evolution (2026-09-04)**: Items 1 and 2 below reflect the initial PostgreSQL-only design rationale.
> These constraints were formally superseded by **[ADR-006: Multi-Dialect Provider Topology](adr-006-multi-dialect-provider-topology.md)**,
> which adopted a Package Segregation strategy to provide first-class support for all major database dialects
> (`PostgreSql`, `SqlServer`, `MySql`, `MariaDb`, `Oracle`, `Sqlite`) and Redis as independent, opt-in packages.
> Items 3–6 remain permanently rejected and in force.

To preserve simplicity, maintainability, portability, and Native AOT compatibility, the following were rejected during initial design:

1. ~~**Redis Backend (Redlock)**~~: *(Initial rationale)* Rejected as core dependency. Contradicted the "zero additional infrastructure" principle. Adding Redis introduces heavy dependencies like `StackExchange.Redis` that complicate trimming and add another cluster to maintain.
   **→ Superseded by ADR-006**: Available as `EricksonLopez.DistributedLock.Redis` (opt-in).
2. ~~**SQL Server / MySQL / Oracle Backends**~~: *(Initial rationale)* Rejected from core. The platform was PostgreSQL-first.
   **→ Superseded by ADR-006**: All dialects available as separate opt-in packages.
3. **Reader-Writer Locks in Core**: Rejected. Shared-lock semantics drastically increase contention complexity and are unjustified by exclusive background worker use cases.
4. **Distributed Semaphores ($N > 1$)**: Rejected. The primary requirement is mutual exclusion ($N = 1$). Rate limiting and bounded parallelism are handled at pipeline or queue layers.
5. **Lock Fencing Tokens**: Active support rejected. Requires native support in downstream storage and adds API complexity. Consistency checks in split-brain scenarios are handled via OCC in `EricksonLopez.Concurrency`.
   > **Revision (2026-09-12)**: The `FencingToken` property was subsequently added to `IDistributedLockHandle` as a **nullable extension point with a default interface implementation of `null`**. This distinguishes the design intent: *active monotonic fencing token support remains rejected* in all current providers, but the contract surface is reserved for future providers that can natively supply monotonic tokens. Consumers must check for `null` before using `FencingToken`.
6. **Connection Multiplexing for Session Locks**: Rejected. Multiplexing multiple advisory locks on a single socket causes cross-contention and complicates deterministic fault recovery. Dedicated connections guarantee isolation.

---

## 4. Consequences

### Positive
- **Exact-Once Execution**: Eliminates batch task overlap and enforces mutual exclusion cluster-wide.
- **Zero Additional Infrastructure**: Leverages the existing PostgreSQL engine without managing Redis, Consul, or ZooKeeper clusters.
- **100% Native AOT & Trimming Compliant**: Native compilation without reflection or trimming warnings using pure ADO.NET commands.
- **Operational Resilience**: Proactive session-loss detection via keepalive and cooperative cancellation via `HandleLostToken`.
- **Seamless Ecosystem Integration**: Synergizes cleanly with `EricksonLopez.Result`, `EricksonLopez.Transaction`, and `EricksonLopez.Concurrency`.

### Negative and Mitigations
- **Connection Consumption**: Each active session lock holds a dedicated physical connection.
  *Mitigation*: Encourage transaction-level locks (`pg_advisory_xact_lock`) and minimize hold duration of session locks.
- **Database Dependency**: If PostgreSQL restarts or becomes saturated, distributed locking degrades alongside the database.
  *Mitigation*: Expected behavior in standard architectures; background workers require the database to persist state anyway.

---

[Index](README.md) | [Next: ADR-002 →](adr-002-distributed-lock-options-and-di-registration.md)
