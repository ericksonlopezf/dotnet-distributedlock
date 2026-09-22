# ADR-007: Test Project Symmetry and Architecture Rules

## Status
Accepted

## Date
2026-09-04

**Context**: Test Project Symmetry, Native AOT Verification, and Architectural Governance  
**Decision Makers**: Principal Software Architect, Core Infrastructure Team  

---

[← Previous: ADR-006](adr-006-multi-dialect-provider-topology.md) | [Index](README.md)

---

## 1. Context and Problem Statement

In alignment with the modularity, traceability, and code quality standards of the **EricksonLopez.*** ecosystem (formalized in ADR-017 across foundational libraries), an exact bijective (1:1) symmetry is required between production packages in `src/` and unit test projects in `tests/`.

Previously, the repository used a monolithic test structure in `tests/EricksonLopez.DistributedLock.Tests` that combined abstraction tests, PostgreSQL provider unit tests, and Docker-dependent integration tests into a single test project. This violated architectural symmetry, hindered selective test suite execution in CI/CD pipelines, and obscured per-dialect code coverage reporting.

---

## 2. Decision

We establish strict **1:1 symmetry** between each production package and its corresponding unit test project, alongside specialized test projects for container-based integration, architecture rules, and Native AOT compilation:

### 2.1. Canonical 1:1 Symmetry Mapping

```
src/
├── EricksonLopez.DistributedLock.Abstractions/       <──> tests/EricksonLopez.DistributedLock.Abstractions.Tests/
├── EricksonLopez.DistributedLock.PostgreSql/        <──> tests/EricksonLopez.DistributedLock.PostgreSql.Tests/
├── EricksonLopez.DistributedLock.SqlServer/         <──> tests/EricksonLopez.DistributedLock.SqlServer.Tests/
├── EricksonLopez.DistributedLock.MySql/             <──> tests/EricksonLopez.DistributedLock.MySql.Tests/
├── EricksonLopez.DistributedLock.MariaDb/           <──> tests/EricksonLopez.DistributedLock.MariaDb.Tests/
├── EricksonLopez.DistributedLock.Oracle/            <──> tests/EricksonLopez.DistributedLock.Oracle.Tests/
├── EricksonLopez.DistributedLock.Sqlite/            <──> tests/EricksonLopez.DistributedLock.Sqlite.Tests/
└── EricksonLopez.DistributedLock.Redis/             <──> tests/EricksonLopez.DistributedLock.Redis.Tests/

End-to-End Integration, Architecture Enforcement, and Native AOT:
├── tests/EricksonLopez.DistributedLock.IntegrationTests/
├── tests/EricksonLopez.DistributedLock.ArchitectureTests/
└── tests/EricksonLopez.DistributedLock.AotSmokeTest/
```

### 2.2. Responsibilities by Test Category

1. **Unit Test Projects (`*.Tests`)**:
   - Ultra-fast execution times (under 500ms per test suite).
   - No reliance on external daemons, Docker, or live network sockets.
   - Utilize test doubles and fakes (`NSubstitute`) to validate parameter guards, options validation, interface contracts, and cooperative cancellation.
   - Mandatory execution across all supported runtime targets (`net8.0`, `net9.0`, `net10.0`).

2. **Integration Test Project (`IntegrationTests`)**:
   - Explicitly isolated via xUnit traits `[Trait("Category", "Integration")]`.
   - Utilizes `Testcontainers` to provision live containerized database instances (e.g., PostgreSQL) or high-concurrency embedded engines (e.g., shared SQLite).
   - Validates real mutual exclusion across concurrent connections, polling timeouts under real contention, and transactional release on rollbacks.

3. **Architectural Test Project (`ArchitectureTests`)**:
   - Implemented with `NetArchTest.Rules` and `FluentAssertions`.
   - Validates that `Abstractions` maintains zero dependencies on concrete drivers or adapters.
   - Validates that no dialect adapter references another dialect or foreign driver.
   - Validates that all concrete implementations of `IDistributedLockProvider` and `IDistributedLockHandle` are `sealed` classes to enforce devirtualization and Native AOT optimization.
   - Validates uniform naming conventions (`*LockOptions`, `I` prefix for interfaces, `EricksonLopez.DistributedLock.*` assembly naming).
   - Validates zero usage of members or types marked with `[Obsolete]`.
   - Validates repository hygiene, kebab-case documentation rules, and support email availability across key documents.

4. **Native AOT Smoke Test (`AotSmokeTest`)**:
   - Standalone console executable configured with `<PublishAot>true</PublishAot>` and `<EnableTrimAnalyzer>true</EnableTrimAnalyzer>`.
   - Validates zero trimming warnings (`IL2026`, `IL3050`).
   - Executes a physical end-to-end lock acquisition, contention, and release cycle at runtime with SQLite in-memory to prove zero undeclared dynamic reflection.

### 2.3. Decommissioning Rules
- The legacy monolithic project `tests/EricksonLopez.DistributedLock.Tests` is permanently decommissioned and purged from the repository tree and `EricksonLopez.DistributedLock.slnx`.

---

## 3. Consequences

### Positive
- **Immediate Traceability**: Modifications in `src/<Project>` are validated immediately by executing only `tests/<Project>.Tests`.
- **CI/CD Parity and Parallelism**: Pipelines execute all unit test suites concurrently in seconds without requiring Docker environments.
- **Continuous Architectural Governance**: Layering boundaries, sealing rules, and naming conventions are verified automatically on every build.
- **Verified Native AOT Assurance**: Dynamic reflection regressions and unannotated generic expansions are prevented by `AotSmokeTest`.
- **Fault Isolation**: Dialect-specific bugs or driver regressions are isolated to their dedicated test project without destabilizing the rest of the solution.

---

[← Previous: ADR-006](adr-006-multi-dialect-provider-topology.md) | [Index](README.md)
