# Official Showcase & Reference Implementation — EricksonLopez.DistributedLock

> **Living Documentation, Executable Reference Architecture, and Progressive Scenarios**  
> **Location**: [`samples/EricksonLopez.DistributedLock.Showcase`](../samples/EricksonLopez.DistributedLock.Showcase) | **Target Runtimes**: .NET 8.0, .NET 9.0, .NET 10.0

---

## 1. Overview

The **Showcase project** (`samples/EricksonLopez.DistributedLock.Showcase`) serves as the official, executable source of truth for the `EricksonLopez.DistributedLock` ecosystem. It demonstrates real production contracts and behaviors without relying on artificial test doubles or mocks.

The application contains two structured learning tracks:
1. **11 Progressive Scenarios (Levels 00 to 10)**: Covering fundamental concurrency concepts, DI configuration, multi-dialect support, fencing tokens, and Clean Architecture pipeline integration.
2. **12 Production Recipes (Cookbook)**: Standalone executable examples of common enterprise architectural patterns.

---

## 2. How to Run the Showcase

The Showcase application supports interactive console menu execution as well as headless automated batch runs for CI/CD smoke testing.

### Automated Full Suite Execution (Smoke Test Mode)

Run the entire showcase suite across all supported runtime targets:

```bash
# Execute on .NET 8.0
dotnet run --project samples/EricksonLopez.DistributedLock.Showcase --framework net8.0 -- --all

# Execute on .NET 9.0
dotnet run --project samples/EricksonLopez.DistributedLock.Showcase --framework net9.0 -- --all

# Execute on .NET 10.0
dotnet run --project samples/EricksonLopez.DistributedLock.Showcase --framework net10.0 -- --all
```

### Running a Specific Progressive Level

```bash
# Run Level 3 (Real-World Use Cases: Banking Deduplication & Webhooks)
dotnet run --project samples/EricksonLopez.DistributedLock.Showcase --framework net10.0 -- --level 3
```

### Running a Specific Recipe

```bash
# Run Recipe 4 (Database Transaction-Bound Advisory Locks)
dotnet run --project samples/EricksonLopez.DistributedLock.Showcase --framework net10.0 -- --recipe 4
```

### Interactive Console Mode

Launch without arguments to navigate scenarios interactively via console prompts:

```bash
dotnet run --project samples/EricksonLopez.DistributedLock.Showcase --framework net10.0
```

---

## 3. Progressive Learning Curriculum (Levels 00 – 10)

| Level | Name | Source File | Core Architectural Concepts Demonstrated |
|:---:|---|---|---|
| **00** | Conceptual Foundations | [`Level00_Conceptual.cs`](../samples/EricksonLopez.DistributedLock.Showcase/Scenarios/Level00_Conceptual.cs) | Cluster concurrency anomalies, why `lock` / `Monitor` fails across processes, relational advisory locks vs. Redis TTL leases. |
| **01** | Quick Start & DI | [`Level01_QuickStart.cs`](../samples/EricksonLopez.DistributedLock.Showcase/Scenarios/Level01_QuickStart.cs) | Service collection registration (`AddSqliteDistributedLock`), `TryAcquireAsync`, deterministic `await using` scope release. |
| **02** | Full Configuration | [`Level02_FullConfiguration.cs`](../samples/EricksonLopez.DistributedLock.Showcase/Scenarios/Level02_FullConfiguration.cs) | All dialect options: `SqliteLockOptions`, `PostgresLockOptions`, `SqlServerLockOptions`, `MySqlLockOptions`, `MariaDbLockOptions`, `OracleLockOptions`, `RedisLockOptions`. Backoff jitter, and all three `ExecuteWithLockAsync` overloads. |
| **03** | Real-World Use Cases | [`Level03_RealWorldUseCases.cs`](../samples/EricksonLopez.DistributedLock.Showcase/Scenarios/Level03_RealWorldUseCases.cs) | Double-spending prevention in banking ledger systems and strict webhook deduplication using `ExecuteWithLockAsync<T>`. |
| **04** | Transactional Integration | [`Level04_TransactionalIntegration.cs`](../samples/EricksonLopez.DistributedLock.Showcase/Scenarios/Level04_TransactionalIntegration.cs) | ADO.NET `IDbTransaction` scoped locking (`TryAcquireInTransactionAsync`), PgBouncer transaction-pooling safety, automatic release on commit/rollback. |
| **05** | High Concurrency Contention | [`Level05_ConcurrentProcessing.cs`](../samples/EricksonLopez.DistributedLock.Showcase/Scenarios/Level05_ConcurrentProcessing.cs) | 10 concurrent tasks competing for a single lock key, bounded timeouts, jittered backoff, and strict mutual exclusion verification. |
| **06** | Railway Error Handling | [`Level06_ErrorHandling.cs`](../samples/EricksonLopez.DistributedLock.Showcase/Scenarios/Level06_ErrorHandling.cs) | Exhaustive classification of `DistributedLockErrors` (`LockAlreadyHeld`, `Timeout`, `Canceled`, `LockLost`) using `Result<T>` without runtime exceptions. |
| **07** | Socket Severance Detection | [`Level07_HandleLostToken.cs`](../samples/EricksonLopez.DistributedLock.Showcase/Scenarios/Level07_HandleLostToken.cs) | Long-running worker cooperative abort using `IDistributedLockHandle.HandleLostToken` during simulated TCP connection termination. |
| **08** | Fencing & Custom Decoration | [`Level08_FencingTokensAndCustomization.cs`](../samples/EricksonLopez.DistributedLock.Showcase/Scenarios/Level08_FencingTokensAndCustomization.cs) | Kleppmann fencing tokens (`IDistributedLockHandle.FencingToken`) protecting shared storage against GC pauses, and decorator pattern extension. |
| **09** | Multi-Engine Dialect Matrix | [`Level09_MultiEngineShowcase.cs`](../samples/EricksonLopez.DistributedLock.Showcase/Scenarios/Level09_MultiEngineShowcase.cs) | Total API parity: Demonstrates consuming `IDistributedLockProvider` identically across PostgreSQL, SQL Server, MySQL, MariaDB, Oracle, SQLite, and Redis. |
| **10** | Enterprise Clean Architecture | [`Level10_EnterpriseArchitecture.cs`](../samples/EricksonLopez.DistributedLock.Showcase/Scenarios/Level10_EnterpriseArchitecture.cs) | Inspecting `[DistributedLock]` metadata inside MediatR / CQRS pipeline behaviors for zero-boilerplate command synchronization. |

---

## 4. Executable Production Recipes (Cookbook)

1. **Recipe 01**: [Immediate Non-Blocking Attempt](../samples/EricksonLopez.DistributedLock.Showcase/Cookbook/Recipe01_NonBlockingAttempt.cs) — Skipping contested maintenance runs without thread suspension.
2. **Recipe 02**: [Bounded Timeout with Jittered Backoff](../samples/EricksonLopez.DistributedLock.Showcase/Cookbook/Recipe02_BoundedTimeoutRetry.cs) — Tolerating short contention windows while preventing thundering herds.
3. **Recipe 03**: [Safe Declarative Scope Guard](../samples/EricksonLopez.DistributedLock.Showcase/Cookbook/Recipe03_SafeScopeGuard.cs) — Atomic execution with `ExecuteWithLockAsync<T>`.
4. **Recipe 04**: [Transactional Advisory Lock](../samples/EricksonLopez.DistributedLock.Showcase/Cookbook/Recipe04_TransactionalAdvisoryLock.cs) — Binding locks directly to the lifecycle of an active `DbTransaction`.
5. **Recipe 05**: [Worker Loss Detection](../samples/EricksonLopez.DistributedLock.Showcase/Cookbook/Recipe05_WorkerLossDetection.cs) — Linking `CancellationTokenSource` with `HandleLostToken` for fail-fast aborts.
6. **Recipe 06**: [Fencing Token Storage Invalidation](../samples/EricksonLopez.DistributedLock.Showcase/Cookbook/Recipe06_FencingTokenValidation.cs) — Rejecting stale writes in downstream databases.
7. **Recipe 07**: [OpenTelemetry Metrics Observability](../samples/EricksonLopez.DistributedLock.Showcase/Cookbook/Recipe07_OpenTelemetryIntegration.cs) — Capturing counters and histograms with `MeterListener`.
8. **Recipe 08**: [Multi-Database Dialect Switching](../samples/EricksonLopez.DistributedLock.Showcase/Cookbook/Recipe08_MultiDbDialectSwitching.cs) — Seamlessly alternating between SQLite and enterprise database engines.
9. **Recipe 09**: [Declarative Pipeline Attribute Processor](../samples/EricksonLopez.DistributedLock.Showcase/Cookbook/Recipe09_DeclarativeAttributeProcessor.cs) — Automating locking via `DistributedLockAttribute`.
10. **Recipe 10**: [Graceful Host Shutdown Handling](../samples/EricksonLopez.DistributedLock.Showcase/Cookbook/Recipe10_HandlingCancellationAndShutdown.cs) — Cleanly aborting pending acquisitions on SIGTERM.
11. **Recipe 11**: [Blocking Acquire Overloads](../samples/EricksonLopez.DistributedLock.Showcase/Cookbook/Recipe11_BlockingAcquireOverloads.cs) — All five `AcquireAsync` / `AcquireHandleAsync` / `ExecuteWithLockAsync(timeout)` blocking overloads demonstrated.
12. **Recipe 12**: [Transaction Lock Blocking Overloads](../samples/EricksonLopez.DistributedLock.Showcase/Cookbook/Recipe12_TransactionLockBlockingOverloads.cs) — All `SqliteTransactionLockExtensions` overloads: `IDbTransaction`, `DbTransaction`, `TryAcquire`, and `Acquire` (blocking).
