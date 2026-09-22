# Architecture Decision Records (ADRs)

This catalog documents the formal sequence of architectural decisions, non-functional requirements, resilience guarantees, and systematic design discards for **EricksonLopez.DistributedLock**.

> [!NOTE]
> The ADRs in this repository maintain their **own local autonomous sequence** (`adr-001` through `adr-007`). For cross-cutting traceability within the broader `EricksonLopez.*` macroarchitecture ecosystem, foundational decision `adr-001` corresponds to global identifier **ADR-015**.

---

## Sequential Architectural Decisions Index

| ADR | Title | Status | Date | Context / Summary |
|:---:|---|:---:|:---:|---|
| [**ADR-001**](adr-001-distributed-lock-abstraction.md) | Distributed Lock Abstraction *(Global: ADR-015)* | `Accepted` | 2026-08-30 (Rev. 2026-09-03) | Tier 0 abstraction contract, Railway-Oriented Programming error modeling, PostgreSQL advisory locks, SHA-256 mathematical derivation, PgBouncer rules, and permanent core discards. |
| [**ADR-002**](adr-002-distributed-lock-options-and-di-registration.md) | Distributed Lock Options and DI Registration | `Accepted` | 2026-09-03 | Strongly typed configuration (`PostgresLockOptions`), thundering herd mitigation via exponential polling with randomized jitter, and idiomatic extensions for `IServiceCollection`. |
| [**ADR-003**](adr-003-session-lock-keepalive-and-handle-lost-token.md) | Session Lock Keepalive and Handle Lost Token | `Accepted` | 2026-09-03 | Active heartbeat liveness monitoring with `PeriodicTimer` (`SELECT 1;`) and cooperative cancellation via `HandleLostToken` on TCP socket termination. |
| [**ADR-004**](adr-004-opentelemetry-metrics-and-observability.md) | OpenTelemetry Metrics and Observability | `Accepted` | 2026-09-03 | Native zero-allocation BCL diagnostic instrumentation via `System.Diagnostics.Metrics.Meter` ("EricksonLopez.DistributedLock"). |
| [**ADR-005**](adr-005-transaction-and-mediator-ecosystem-integration.md) | Transaction and Mediator Ecosystem Integration | `Accepted` | 2026-09-03 | Clean Architecture co-evolution with `EricksonLopez.Transaction` and `EricksonLopez.Mediator` (automatic transactional locks via `IDbTransaction` and declarative `[DistributedLock]` attribute). |
| [**ADR-006**](adr-006-multi-dialect-provider-topology.md) | Multi-Dialect Provider Topology and Ecosystem Database Parity | `Accepted` | 2026-09-04 | Comprehensive database dialect support across the ecosystem (PostgreSQL, SQL Server, MySQL, MariaDB, Oracle, SQLite, and Redis) with strict Native AOT package segregation. |
| [**ADR-007**](adr-007-test-project-symmetry-and-architecture-rules.md) | Test Project Symmetry and Architecture Rules | `Accepted` | 2026-09-04 | Bijective 1:1 structural symmetry between production projects in `src/` and unit test suites in `tests/`, segregating container-based integration tests. |
| [**ADR-008**](adr-008-architectural-reconciliation-and-documentation-governance.md) | Architectural Reconciliation and Documentation Governance | `Accepted` | 2026-09-12 | Documentation standardization, lowercase kebab-case enforcement, technical English normalization, and community health architecture. |

---

## Decision Guidelines and Format

Any structural modification to public contracts, addition of new backend providers, or ecosystem integration extensions must be formalized via a new sequentially numbered ADR in this local catalog (`adr-008`, etc.).
