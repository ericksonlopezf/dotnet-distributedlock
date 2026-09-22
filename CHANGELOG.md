# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).


## [Unreleased]

## [1.0.0] - 2026-09-21

### Added
- **`EricksonLopez.DistributedLock.Abstractions`**:
  - `IDistributedLockProvider` contract with non-blocking (`TryAcquireAsync`), polling with jittered exponential backoff, and blocking (`AcquireAsync`) overloads.
  - `IDistributedLockHandle` interface exposing `HandleLostToken`, `ResourceId`, `LockId`, and `FencingToken`.
  - `AsyncDisposableHandleAdapter` providing backward-compatible handle adaptation for generic `IAsyncDisposable` implementations.
  - Strongly-typed `DistributedLockErrors` (`LockAlreadyHeld`, `Timeout`, `LockLost`, `Canceled`) based on `EricksonLopez.Result`.
  - Declarative `[DistributedLock]` attribute for integration with `EricksonLopez.Mediator` pipeline behaviors.
  - Safe execution extensions `ExecuteWithLockAsync` and `ExecuteWithLockAsync<T>` with automatic `HandleLostToken` linking and explicit timeout support (`TimeSpan timeout`).
- **Relational & Cache Dialect Providers**:
  - `EricksonLopez.DistributedLock.PostgreSql`: High-throughput advisory locks (`pg_try_advisory_lock`, `pg_advisory_lock`, `pg_try_advisory_xact_lock`), background `PeriodicTimer` keepalive heartbeats, and OpenTelemetry instrumentation.
  - `EricksonLopez.DistributedLock.SqlServer`: Application locks via `sp_getapplock` / `sp_releaseapplock` with full session and transaction lifecycles.
  - `EricksonLopez.DistributedLock.MySql`: MySQL user-level locks via `GET_LOCK` and `RELEASE_LOCK` with MySqlConnector.
  - `EricksonLopez.DistributedLock.MariaDb`: MariaDB user-level locks via `GET_LOCK` and `RELEASE_LOCK` with MySqlConnector.
  - `EricksonLopez.DistributedLock.Oracle`: Oracle Database locks via `DBMS_LOCK` with Oracle.ManagedDataAccess.Core.
  - `EricksonLopez.DistributedLock.Sqlite`: SQLite database-enforced concurrency locks via `Microsoft.Data.Sqlite`.
  - `EricksonLopez.DistributedLock.Redis`: Redis distributed mutual exclusion via StackExchange.Redis with Lua scripts for atomic release and lease renewal.
- **Showcase & Cookbook**:
  - Living reference sample project with 11 progressive learning levels and 12 production-ready recipes demonstrating all provider configurations, blocking overloads, and transactional extensions.
- **Testing, Governance & Quality**:
  - 1:1 dedicated test project symmetry for each production package.
  - Comprehensive architectural enforcement suite (`NetArchTest.Rules`) validating Native AOT readiness, dialect decoupling, and zero obsolete APIs.
  - Integration test suite running against real database engines via Testcontainers.
  - Native AOT smoke test executable (`AotSmokeTest`) verifying trim-safety and zero dynamic reflection warnings.
  - Standardized technical documentation in `/docs/` with normalized kebab-case naming, portable links, and ADR-008 quality thresholds.
  - Complete community health governance with `SUPPORT.md`, `CODEOWNERS`, and GitHub issue/PR templates.
- **Build & Packaging**:
  - Automated conditional Strong-Name Signing support in `Directory.Build.props` when `EricksonLopez.snk` is detected.
  - Hardened GitHub Actions CI/CD workflows (`ci.yml`, `publish.yml`, `mutation-testing.yml`, `aot-smoke-test.yml`) with immutable commit SHA pinning.

