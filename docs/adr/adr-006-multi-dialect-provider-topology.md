# ADR-006: Multi-Dialect Provider Topology and Ecosystem Database Parity

## Status
Accepted

## Date
2026-09-04

**Context**: Ecosystem Database Parity and Dialect Decoupling  
**Decision Makers**: Principal Software Architect, Core Infrastructure Team  

---

[← Previous: ADR-005](adr-005-transaction-and-mediator-ecosystem-integration.md) | [Index](README.md) | [Next: ADR-007 →](adr-007-test-project-symmetry-and-architecture-rules.md)

---

## 1. Context and Problem Statement

The canonical architectural standard of the **EricksonLopez.*** ecosystem (formalized in ADR-016 across `dotnet-transaction`, `dotnet-concurrency`, `dotnet-idempotency`, etc.) mandates that any infrastructure coordination component provide first-class support for all primary relational database engines and distributed storage dialects prevalent in enterprise environments:

1. **PostgreSQL** (`Npgsql`)
2. **Microsoft SQL Server** (`Microsoft.Data.SqlClient`)
3. **MySQL** (`MySqlConnector`)
4. **MariaDB** (`MySqlConnector`)
5. **Oracle** (`Oracle.ManagedDataAccess.Core`)
6. **SQLite** (`Microsoft.Data.Sqlite`)
7. **Redis** (`StackExchange.Redis`)

Originally, `EricksonLopez.DistributedLock` provided only the single provider package `EricksonLopez.DistributedLock.PostgreSql`. To achieve complete ecosystem parity and match competitive libraries (such as *DistributedLock* by Medallion), it was essential to decouple and implement independent, high-throughput, zero-allocation providers for every major database dialect.

---

## 2. Decision

We adopt a decoupled **Package Segregation** topology, where each database dialect resides in its own production package under `src/`, depending exclusively on the Tier 0 abstraction contract:

### 2.1. Project Breakdown and Native Locking Primitives

| Package | Provider Driver | Native Locking Mechanism | Release Semantics |
|---|---|---|---|
| `EricksonLopez.DistributedLock.Abstractions` | BCL / Pure | `IDistributedLockProvider`, `IDistributedLockHandle`, `DistributedLockErrors` | ROP contracts and interfaces |
| `EricksonLopez.DistributedLock.PostgreSql` | `Npgsql` | `pg_try_advisory_lock` / `pg_try_advisory_xact_lock` | `pg_advisory_unlock` or transaction completion |
| `EricksonLopez.DistributedLock.SqlServer` | `Microsoft.Data.SqlClient` | `sys.sp_getapplock` (`LockOwner = 'Session'` or `'Transaction'`) | `sys.sp_releaseapplock` or transaction completion |
| `EricksonLopez.DistributedLock.MySql` | `MySqlConnector` | `SELECT GET_LOCK(@Resource, @Timeout)` | `SELECT RELEASE_LOCK(@Resource)` |
| `EricksonLopez.DistributedLock.MariaDb` | `MySqlConnector` | `SELECT GET_LOCK(@Resource, @Timeout)` | `SELECT RELEASE_LOCK(@Resource)` |
| `EricksonLopez.DistributedLock.Oracle` | `Oracle.ManagedDataAccess.Core` | `DBMS_LOCK.ALLOCATE_UNIQUE` + `DBMS_LOCK.REQUEST` | `DBMS_LOCK.RELEASE` or session termination |
| `EricksonLopez.DistributedLock.Sqlite` | `Microsoft.Data.Sqlite` | Concurrency table `__distributed_locks` with `PRIMARY KEY` constraints | `DELETE FROM __distributed_locks` |
| `EricksonLopez.DistributedLock.Redis` | `StackExchange.Redis` | `SET resource token NX PX expiry` | Atomic Lua script validation and `DEL` |

### 2.2. Design Invariants and Coherence

1. **Zero Circular or Leaked Dependencies**: No dialect package references another dialect package. All dialects strictly reference `EricksonLopez.DistributedLock.Abstractions`.
2. **Strict Native AOT and Trimming Compliance**: All projects configure `IsAotCompatible=true`, `EnableTrimAnalyzer=true`, and `TreatWarningsAsErrors=true`, compiling cleanly with zero trimming warnings.
3. **Modern Multi-Targeting**: Simultaneous compilation across `net8.0`, `net9.0`, and `net10.0`.
4. **Cancellation and Disconnection Resilience**: All providers verify cooperative `CancellationToken` state both prior to issuing queries and across polling loops, deterministically returning `DistributedLockErrors.Canceled`.
5. **Idiomatic Dependency Injection Extensions**: Each package exposes `Add<Dialect>DistributedLock` extension methods on `IServiceCollection`, supporting strongly typed configuration via `Action<<Dialect>LockOptions>`.
6. **Transactional Lock Support**: Relational dialects (`PostgreSql`, `SqlServer`, `MySql`, `MariaDb`, `Oracle`, `Sqlite`) provide extension methods on `IDbTransaction` (`TryAcquireInTransactionAsync` and `AcquireInTransactionAsync`) to bind lock lifetimes directly to the active database transaction scope.
7. **OpenTelemetry Observability**: Established in `EricksonLopez.DistributedLock.PostgreSql` via `DistributedLockMetrics` (`System.Diagnostics.Metrics`, MeterName: `"EricksonLopez.DistributedLock"`). Expanding native metrics to remaining dialects is scheduled for subsequent iterations.

---

## 3. Consequences

### Positive
- **Complete Ecosystem Parity**: 100% cohesion with database dialects supported across `dotnet-transaction`, `dotnet-concurrency`, and `dotnet-idempotency`.
- **Architectural Flexibility**: Consumers install only the driver package required for their environment (e.g., SQLite for local unit testing, PostgreSQL or SQL Server in production clusters, Redis for decoupled microservices).
- **Maximum Throughput**: Each dialect leverages the optimal native locking primitives provided by its underlying storage engine.

### Permanent Discards
- **Rejected**: Implementing a single monolithic package bundling all drivers (`Microsoft.Data.SqlClient`, `Npgsql`, `Oracle.ManagedDataAccess.Core`, etc.). This causes massive dependency bloat, assembly conflicts, and trimmer violations in Native AOT.
- **Rejected**: Busy-wait CPU spinlocks for polling. All polling retry loops utilize non-blocking asynchronous delays with randomized jitter.

---

[← Previous: ADR-005](adr-005-transaction-and-mediator-ecosystem-integration.md) | [Index](README.md) | [Next: ADR-007 →](adr-007-test-project-symmetry-and-architecture-rules.md)
