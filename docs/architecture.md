# Architecture Guide — EricksonLopez.DistributedLock

> **Architectural Specification, Component Topology, Distributed Invariants, and State Models**  
> **Version**: 1.0.0 | **Ecosystem**: `EricksonLopez.*` | **Target Runtimes**: .NET 8.0, .NET 9.0, .NET 10.0 | **Invariants**: 100% Native AOT, Zero-Allocation Result Pattern, Zero `[Obsolete]` Policy

---

## 1. System Overview

`EricksonLopez.DistributedLock` is a high-performance, Tier 0 distributed mutual exclusion and cluster coordination framework engineered for modern cloud-native .NET systems. It coordinates mission-critical background jobs, transactional workflows, event-driven message consumers, and batch processing across distributed replicas without requiring expensive external coordination clusters (such as Apache ZooKeeper or HashiCorp Consul).

### Core Architectural Principles

1. **Pure Tier 0 Abstraction**: Zero coupling between domain/application code and underlying storage engines. Core contracts (`IDistributedLockProvider`, `IDistributedLockHandle`, `DistributedLockErrors`) reside in `EricksonLopez.DistributedLock.Abstractions` with zero concrete database driver dependencies.
2. **Railway-Oriented Result Pattern**: Lock contention, timeouts, and cancellations return strongly typed `Result<IAsyncDisposable>` or `Result<IDistributedLockHandle>` structures powered by `EricksonLopez.Result`. Control flow never throws exceptions on expected contention, eliminating CPU thread starvation and stack unwinding overhead.
3. **Native Engine Storage Primitives**: Rather than imposing artificial coordination tables where native mechanisms exist, the suite leverages native database-level primitives:
   - **PostgreSQL**: Session and transaction advisory locks (`pg_try_advisory_lock`, `pg_advisory_xact_lock`).
   - **SQL Server**: Distributed application locks (`sp_getapplock`, `sp_releaseapplock`).
   - **MySQL & MariaDB**: User-level locks (`GET_LOCK`, `RELEASE_LOCK`) via `MySqlConnector`.
   - **Oracle**: Named application locks (`DBMS_LOCK.REQUEST`, `DBMS_LOCK.RELEASE`).
   - **SQLite**: Atomic coordination table (`__distributed_locks`) with millisecond TTL expiration.
   - **Redis**: Atomic mutex (`SET NX PX`), Lua script release, and periodic lease renewals.
4. **Active Keepalive & Socket Drop Detection**: Session locks run background heartbeat monitors (`PeriodicTimer`) that detect network partitions or socket severance, proactively signaling cooperative task aborts via `IDistributedLockHandle.HandleLostToken`.
5. **100% Native AOT & Trimming Compliant**: Zero runtime reflection, zero dynamic IL emission, and `sealed` concrete types to optimize devirtualization and binary trimming across .NET 8, 9, and 10.

---

## 2. Package Topology & Clean Architecture Layering

The system strictly enforces the Dependency Inversion Principle. Application and domain layers only reference the Tier 0 Abstractions, while concrete infrastructure dialect packages implement the provider contract.

```mermaid
graph TD
    subgraph Consumers["Application & Domain Layers"]
        Worker["Background Workers / Cron Jobs"]
        Api["Minimal APIs / Web Controllers"]
        Mediator["Command Pipeline (MediatR / EricksonLopez.Mediator)"]
    end

    subgraph Core["Tier 0 Abstractions (EricksonLopez.DistributedLock.Abstractions)"]
        Contract["IDistributedLockProvider"]
        HandleContract["IDistributedLockHandle"]
        ResultPattern["Result<T> / DistributedLockErrors"]
        Attr["[DistributedLock] Attribute"]
        Metrics["DistributedLockMetrics (OpenTelemetry Meter)"]
    end

    subgraph Adapters["Infrastructure Provider Adapters"]
        Postgres["EricksonLopez.DistributedLock.PostgreSql<br/>(Advisory Locks)"]
        SqlServer["EricksonLopez.DistributedLock.SqlServer<br/>(sp_getapplock)"]
        MySQL["EricksonLopez.DistributedLock.MySql<br/>(GET_LOCK)"]
        MariaDB["EricksonLopez.DistributedLock.MariaDb<br/>(GET_LOCK)"]
        Oracle["EricksonLopez.DistributedLock.Oracle<br/>(DBMS_LOCK)"]
        Sqlite["EricksonLopez.DistributedLock.Sqlite<br/>(Atomic Table)"]
        Redis["EricksonLopez.DistributedLock.Redis<br/>(Lua Script Mutex)"]
    end

    subgraph StorageEngines["Underlying Storage Substrates"]
        PgEngine[("PostgreSQL 14+")]
        MsSqlEngine[("SQL Server 2019+")]
        MySqlEngine[("MySQL 8.0+")]
        MariaDbEngine[("MariaDB 10.6+")]
        OracleEngine[("Oracle 19c+ / 23ai")]
        SqliteEngine[("SQLite File / In-Memory")]
        RedisEngine[("Redis 7.0+ / Valkey")]
    end

    Consumers --> Core
    Adapters --> Core
    Postgres --> PgEngine
    SqlServer --> MsSqlEngine
    MySQL --> MySqlEngine
    MariaDB --> MariaDbEngine
    Oracle --> OracleEngine
    Sqlite --> SqliteEngine
    Redis --> RedisEngine
```

---

## 3. Internal Dependency Graph

All provider packages depend strictly on `EricksonLopez.DistributedLock.Abstractions`. No circular references or cross-provider dependencies are permitted:

```mermaid
graph LR
    Abstractions["EricksonLopez.DistributedLock.Abstractions<br/>(v1.0.0)"]
    Result["EricksonLopez.Result<br/>(v2.0.0)"]

    Abstractions --> Result

    Postgres["DistributedLock.PostgreSql"] --> Abstractions
    SqlServer["DistributedLock.SqlServer"] --> Abstractions
    MySql["DistributedLock.MySql"] --> Abstractions
    MariaDb["DistributedLock.MariaDb"] --> Abstractions
    Oracle["DistributedLock.Oracle"] --> Abstractions
    Sqlite["DistributedLock.Sqlite"] --> Abstractions
    Redis["DistributedLock.Redis"] --> Abstractions
```

---

## 4. Dual Acquisition Modes & Jittered Polling Flow

The framework provides three acquisition paradigms:
1. **Immediate Non-Blocking Attempt (`TryAcquireAsync`)**: Executes a single non-blocking check against the engine.
2. **Bounded Polling with Jittered Exponential Backoff (`TryAcquireAsync(key, timeout)`)**: Repeatedly checks for lock availability while applying randomized exponential backoff to eliminate the "thundering herd" problem.
3. **Database-Level Blocking (`AcquireAsync(key)`)**: Delegates waiting directly to native database engine queues where available (`pg_advisory_lock`, `sp_getapplock`).

```mermaid
sequenceDiagram
    autonumber
    actor Client as Client Application (.NET)
    participant Provider as IDistributedLockProvider
    participant Engine as Storage Substrate (RDBMS / Redis)

    Client->>Provider: TryAcquireAsync("orders:reconciliation", timeout)
    Provider->>Engine: Initial Acquisition Attempt (e.g. pg_try_advisory_lock)
    alt Lock Is Free
        Engine-->>Provider: Acquired = true
        Provider-->>Client: Result.Success(IDistributedLockHandle)
    else Lock Is Contended
        Engine-->>Provider: Acquired = false
        loop Polling with Jittered Exponential Backoff until Timeout
            Provider->>Provider: Task.Delay(CalculateJitteredDelay())
            Provider->>Engine: Retry Acquisition Attempt
            alt Acquired on Retry
                Engine-->>Provider: Acquired = true
                Provider-->>Client: Result.Success(IDistributedLockHandle)
            end
        end
        Provider-->>Client: Result.Failure(DistributedLockErrors.Timeout)
    end
```

---

## 5. Lock Handle Lifecycle and State Machine

Lock handles implement `IDistributedLockHandle` and `IAsyncDisposable`. While held, handles maintain connection liveness, execute keepalive heartbeats, and immediately signal cancellation upon socket termination.

```mermaid
stateDiagram-v2
    [*] --> Unacquired

    Unacquired --> Acquired : TryAcquireAsync() == Success
    Unacquired --> Failed : LockAlreadyHeld / Timeout / Canceled

    state Acquired {
        [*] --> ActiveHolding
        ActiveHolding --> HeartbeatVerification : KeepaliveCadence elapsed
        HeartbeatVerification --> ActiveHolding : Ping succeeded / connection healthy
        HeartbeatVerification --> Lost : Socket severed / failover detected
    }

    ActiveHolding --> Disposed : DisposeAsync() / Scope Exit
    Lost --> Disposed : DisposeAsync()
    Failed --> [*]
    Disposed --> [*]
```

---

## 6. Transactional vs Session-Scoped Locks (PgBouncer Invariant)

PostgreSQL advisory locks illustrate the critical distinction between connection scopes:

- **Session-Level Locks (`pg_advisory_lock`)**: Bound to the physical PostgreSQL backend server process (`backend_pid`).
- **Transaction-Level Locks (`pg_advisory_xact_lock`)**: Bound to the active database transaction, automatically freed on `COMMIT` or `ROLLBACK`.

> [!WARNING]
> **PgBouncer / Connection Pooler Incompatibility**: When running behind PgBouncer with `pool_mode = transaction` or `pool_mode = statement`, session-level locks must **NEVER** be used on pooled connections, as subsequent queries are routed across differing physical server processes. In transactional pooler environments, either:
> 1. Use transaction-scoped locks (`TryAcquireInTransactionAsync`), or
> 2. Connect to a dedicated PgBouncer pool configured with `pool_mode = session`.

```mermaid
sequenceDiagram
    autonumber
    actor App as Application Service
    participant Tx as IDbTransaction
    participant Engine as PostgreSQL Server

    App->>Tx: BeginTransactionAsync()
    App->>Tx: TryAcquireInTransactionAsync("invoice:generate:101")
    Tx->>Engine: SELECT pg_try_advisory_xact_lock(hash)
    Engine-->>Tx: True (Lock Acquired)
    App->>Tx: Execute Business Writes (INSERT / UPDATE)
    alt Commit Transaction
        App->>Tx: CommitAsync()
        Engine-->>Engine: Advisory lock released automatically on commit
    else Rollback Transaction
        App->>Tx: RollbackAsync()
        Engine-->>Engine: Advisory lock released automatically on rollback
    end
```

---

## 7. Fencing Tokens (Kleppmann Storage Invalidation Pattern)

In distributed systems, long Garbage Collection pauses (Stop-The-World) or network partitions can cause a process to lose its lock without realizing it before attempting to write to storage. To guard against stale writes, `IDistributedLockHandle` exposes a monotonic `FencingToken`.

```mermaid
sequenceDiagram
    autonumber
    actor Client1 as Client 1 (Stalled by Full GC)
    actor Client2 as Client 2 (Active Replicant)
    participant LockService as Lock Provider
    participant Storage as Shared Storage (RDBMS / Object Store)

    Client1->>LockService: AcquireAsync("resource") -> FencingToken = 101
    Note over Client1: Client 1 suffers 30s Stop-The-World GC pause
    Note over LockService: Lease expires / Client 1 considered dead
    Client2->>LockService: AcquireAsync("resource") -> FencingToken = 102
    Client2->>Storage: UPDATE data SET token = 102 WHERE token < 102
    Storage-->>Client2: Write Accepted (102 > 0)
    Note over Client1: Client 1 awakens and attempts stale write
    Client1->>Storage: UPDATE data SET token = 101 WHERE token < 101
    Storage-->>Client1: Rejected! (Rows affected = 0, token 101 is stale)
```

---

## 8. Native Observability Architecture (OpenTelemetry)

`EricksonLopez.DistributedLock` produces native zero-allocation BCL metrics using `System.Diagnostics.Metrics.Meter` (`MeterName: EricksonLopez.DistributedLock`):

| Metric Name | Type | Description | Dimension Tags |
|---|:---:|---|---|
| `distributed_lock.acquisitions` | Counter | Total acquisition attempts | `resource_id`, `scope` (`session`/`transaction`), `status` (`acquired`, `already_held`, `timeout`, `canceled`, `error`) |
| `distributed_lock.wait_duration` | Histogram | Milliseconds spent waiting to acquire | `resource_id`, `scope`, `status` |
| `distributed_lock.hold_duration` | Histogram | Milliseconds the lock was held active | `resource_id`, `scope` |
| `distributed_lock.lost_events` | Counter | Instances of unexpected lock severance | `resource_id`, `reason` |

---

## 9. Native AOT & Trimming Invariants

The codebase enforces strict compiler and runtime invariants:
- `<IsAotCompatible>true</IsAotCompatible>` and `<EnableTrimAnalyzer>true</EnableTrimAnalyzer>` enabled in `Directory.Build.props`.
- Zero reflection or dynamic IL emission on critical execution paths.
- All concrete lock provider classes and internal handles are explicitly `sealed`.
- Dedicated Native AOT Smoke Test executable (`tests/EricksonLopez.DistributedLock.AotSmokeTest`) compiled and executed in CI under `/p:PublishAot=true /warnaserror`.
