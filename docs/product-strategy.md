# Product Strategy: EricksonLopez.DistributedLock

> **Version**: 1.0.0 | **Ecosystem**: `EricksonLopez.*` | **Runtime**: .NET 8.0, 9.0, 10.0  
> **Document Status**: Approved Strategy | **Target Audience**: Architects, Core Engineering, Ecosystem Integrators

---

## 1. Product Vision

**EricksonLopez.DistributedLock** is designed to be the foundational distributed locking infrastructure for modern, high-throughput .NET applications. Its primary design philosophy is:

> **Deliver zero-allocation, exception-free distributed mutual exclusion utilizing enterprise database primitives already present in customer infrastructure.**

By eliminating external coordination clusters (such as Consul or ZooKeeper) and leveraging existing databases (PostgreSQL, SQL Server, MySQL, MariaDB, Oracle, SQLite, Redis), teams achieve exact-once execution semantics with zero operational overhead.

---

## 2. Market Positioning & Competitive Advantages

### 1. Modern C# / .NET 10 Alignment
While legacy libraries were architected around .NET Framework 4.5 and throw exceptions upon lock contention, `EricksonLopez.DistributedLock` is built from the ground up for modern .NET (8, 9, 10):
- **Railway-Oriented Programming (`Result<T>`)**: Avoids costly thread suspension and stack traces during expected contention scenarios.
- **Native AOT & Trimming**: 100% compatible with containerized microservices and serverless workloads.
- **`IAsyncDisposable` & `ValueTask`**: Zero allocation during resource release.

### 2. Multi-Dialect Parity
Unlike single-engine libraries (e.g. RedLock.net for Redis only), `EricksonLopez.DistributedLock` provides a unified `IDistributedLockProvider` contract backed by 7 storage engines:
1. **PostgreSQL**: Native session and transaction advisory locks (`pg_advisory_lock`).
2. **SQL Server**: Distributed application locks (`sp_getapplock`).
3. **MySQL & MariaDB**: User-level locks (`GET_LOCK` / `RELEASE_LOCK`).
4. **Oracle**: Database-level named locks (`DBMS_LOCK`).
5. **SQLite**: File-based distributed mutual exclusion (`__distributed_locks`).
6. **Redis**: In-memory distributed lock with atomic Lua release scripts.

### 3. Native Observability
Out-of-the-box OpenTelemetry instrumentation (`System.Diagnostics.Metrics.Meter`) exposing latency histograms and acquisition counters without external SDK coupling.

---

## 3. Product Roadmap

### Phase 1: Core Multi-Dialect Ecosystem (Current — v1.0.0)
- Full support for all 7 dialect providers.
- Strict 1:1 test symmetry across unit, integration, and architecture tests.
- Native AOT validation via `AotSmokeTest`.

### Phase 2: Enhanced OpenTelemetry & Resiliency (v1.1.0)
- Expand OpenTelemetry metrics across all remaining dialect providers.
- Configurable circuit breakers for connection degradation.

### Phase 3: High-Scale Optimizations (v2.0.0)
- Adaptive polling intervals with predictive lock duration modeling.
- Tier 1 orchestration extensions for complex multi-resource lock ordering.

---

## 4. Architectural Boundaries

- **No Business Logic**: `DistributedLock` remains purely an infrastructure-level mutual exclusion primitive.
- **No Direct Persistence Coupling**: Abstractions remain 100% pure without referencing specific database drivers.
- **Clean Architecture Integration**: Idiomatic integration with `EricksonLopez.Transaction` and `EricksonLopez.Mediator` via declarative attributes.
