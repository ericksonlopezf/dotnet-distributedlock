# Framework Testing Roadmap: EricksonLopez.DistributedLock

> **Source of Truth, Execution Guide, Evidence, and Idempotent Tracking Mechanism**  
> **Version**: 1.0.0 | **Ecosystem**: `EricksonLopez.*` | **Runtimes**: .NET 8.0, .NET 9.0, .NET 10.0 | **Invariants**: Native AOT, Zero-Allocation, Thread-Safety

---

## 1. Objectives

1. **Exhaustive Code Coverage**:
   - **Line Coverage**: ≥ 95.00% – 100.00% across all core library logic.
   - **Branch Coverage**: ≥ 85.00% – 100.00% on reachable execution branches.
   - **Method Coverage**: 100.00% on all public, internal, and interface-default methods.
2. **Mutation Testing (Stryker.NET)**:
   - **Empirical Mutation Score**: Measured directly via Stryker.NET reports without artificial inflation.
   - **Break Threshold**: 95% mutation threshold enforced as an automated build gate.
   - **Target Threshold**: ≥ 98% Low, 100% High.
3. **Strict Invariant Preservation**:
   - `IsAotCompatible=true` with zero `IL2026` / `IL3050` warnings verified in Release via `AotSmokeTest`.
   - Zero-allocation hot paths using `Span<T>` / `ReadOnlySpan<T>` and compiler devirtualization via `sealed` types.
   - Strict decoupling verified by `ArchitectureTests` (`NetArchTest.Rules`).
4. **Idempotent Quality Assurance**:
   - Every enhancement, dialect provider, and architectural rule is continuously validated across .NET 8, .NET 9, and .NET 10.

---

## 2. Framework Architecture & Dialect Topology

`EricksonLopez.DistributedLock` provides multivariate distributed mutual exclusion built on `EricksonLopez.Result`:

| Package | Responsibility | Primitives | Status |
|---|---|---|:---:|
| `EricksonLopez.DistributedLock.Abstractions` | Tier 0 Contracts & Error Models | `IDistributedLockProvider`, `IDistributedLockHandle`, `DistributedLockErrors` | Active |
| `EricksonLopez.DistributedLock.PostgreSql` | PostgreSQL Advisory Locks | `pg_try_advisory_lock`, `pg_advisory_lock`, `pg_try_advisory_xact_lock` | Active |
| `EricksonLopez.DistributedLock.SqlServer` | SQL Server Application Locks | `sp_getapplock`, `sp_releaseapplock` | Active |
| `EricksonLopez.DistributedLock.MySql` | MySQL User-Level Locks | `GET_LOCK`, `RELEASE_LOCK` via MySqlConnector | Active |
| `EricksonLopez.DistributedLock.MariaDb` | MariaDB User-Level Locks | `GET_LOCK`, `RELEASE_LOCK` via MySqlConnector | Active |
| `EricksonLopez.DistributedLock.Oracle` | Oracle Database Locks | `DBMS_LOCK.REQUEST`, `DBMS_LOCK.RELEASE` | Active |
| `EricksonLopez.DistributedLock.Sqlite` | SQLite Database Concurrency Locks | Atomic coordination table `__distributed_locks` | Active |
| `EricksonLopez.DistributedLock.Redis` | Redis Distributed Mutex | `SET NX PX`, Lua atomic release and lease renewal | Active |

---

## 3. Test Suite Symmetry (1:1 Mapping)

Every production package under `src/` has a corresponding test suite under `tests/`:

```text
src/
 ├── EricksonLopez.DistributedLock.Abstractions
 ├── EricksonLopez.DistributedLock.PostgreSql
 ├── EricksonLopez.DistributedLock.SqlServer
 ├── EricksonLopez.DistributedLock.MySql
 ├── EricksonLopez.DistributedLock.MariaDb
 ├── EricksonLopez.DistributedLock.Oracle
 ├── EricksonLopez.DistributedLock.Sqlite
 └── EricksonLopez.DistributedLock.Redis

tests/
 ├── EricksonLopez.DistributedLock.Abstractions.Tests
 ├── EricksonLopez.DistributedLock.PostgreSql.Tests
 ├── EricksonLopez.DistributedLock.SqlServer.Tests
 ├── EricksonLopez.DistributedLock.MySql.Tests
 ├── EricksonLopez.DistributedLock.MariaDb.Tests
 ├── EricksonLopez.DistributedLock.Oracle.Tests
 ├── EricksonLopez.DistributedLock.Sqlite.Tests
 ├── EricksonLopez.DistributedLock.Redis.Tests
 ├── EricksonLopez.DistributedLock.ArchitectureTests
 ├── EricksonLopez.DistributedLock.IntegrationTests
 └── EricksonLopez.DistributedLock.AotSmokeTest
```

---

## 4. Stryker.NET Mutation Strategy

### Concurrency and Runner Sizing
- Concurrency is configured to 4 workers by default on GitHub Actions runners (2-core standard runners) to prevent CPU starvation and thread thrashing.
- xUnit test parallelization within each dialect test project is scoped so test runners do not conflict with Stryker's child processes.

### Tiered Execution Model
1. **Pull Requests**:
   - Targeted mutation testing focused on changed packages and their corresponding test assemblies.
   - Stryker `--since:main` incremental execution utilizing cached baseline results.
2. **Main Branch**:
   - Solution-wide matrix execution across all dialect providers.
   - Comprehensive mutation score reporting uploaded to pipeline artifacts.
3. **Nightly Builds**:
   - Deep mutation analysis with extended timeouts and complete mutator coverage.

---

## 5. Architectural Quality Gates

The `EricksonLopez.DistributedLock.ArchitectureTests` suite enforces the following rules:

1. **Abstractions Isolation**: Zero reference from `Abstractions` to concrete database drivers or client libraries.
2. **Dialect Decoupling**: Zero cross-dependencies between dialect provider packages (e.g., `PostgreSql` must not reference `SqlServer` or `Redis`).
3. **Sealed Implementations**: All provider classes, lock handles, and options types must be `sealed` to ensure devirtualization and Native AOT compatibility.
4. **Interface Conventions**: All public interfaces must start with `I`.
5. **Zero Obsolete APIs**: No `[Obsolete]` attributes or deprecated symbols in any source assembly.
6. **One Type Per File**: Every C# file defines at most one top-level type matching the filename.
7. **License Header**: Every C# file begins with `// Copyright © Erickson Lopez. MIT License.`.
8. **Kebab-Case Documentation**: All documentation files adhere to lowercase kebab-case naming.
