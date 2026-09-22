# ADR-005: Transaction and Mediator Ecosystem Integration

## Status
Accepted

## Date
2026-09-03

**Context**: Ecosystem Co-evolution, Clean Architecture, and Declarative Concurrency  
**Decision Makers**: Principal Software Architect, Core Infrastructure Team  

---

[← Previous: ADR-004](adr-004-opentelemetry-metrics-and-observability.md) | [Index](README.md)

---

## 1. Context

Within the `EricksonLopez.*` ecosystem, enterprise applications combine advanced architectural patterns:
1. **Transaction Management**: Coordinated via `EricksonLopez.Transaction`, where data mutations and database operations reside within transactional scopes (`ITransactionContext` / `IDbTransaction`).
2. **Mediator Pattern**: Implemented through `EricksonLopez.Mediator`, utilizing compile-time Roslyn code generators and zero-allocation struct-based pipelines (`IPipelineBehavior`).
3. **Concurrency Control**: `EricksonLopez.Concurrency` (OCC) for entity-level protection, and `EricksonLopez.DistributedLock` (PCC) for cluster-wide mutual exclusion.

Without explicit integration extensions, developers are forced to write repetitive manual lock acquisition blocks inside their handlers or open redundant session connections when an active physical transaction already exists.

---

## 2. Decision

We equip `EricksonLopez.DistributedLock` with **architectural co-evolution** capabilities with other ecosystem libraries without introducing cyclical dependencies:

### 2.1. Level 1 Transactional Integration (`IDbTransaction` / `DbTransaction`)
- Via the `PostgresTransactionLockExtensions` extension class, any active transaction can acquire a pessimistic lock using native `pg_try_advisory_xact_lock` or `pg_advisory_xact_lock` functions:
  ```csharp
  public static Task<Result<IDistributedLockHandle>> TryAcquireInTransactionAsync(
      this IDbTransaction transaction,
      string resourceId,
      ILogger logger,
      CancellationToken cancellationToken = default);
  ```
- **Lifecycle Benefit**: Requires neither a dedicated connection nor explicit `DisposeAsync()` calls. The PostgreSQL engine automatically releases the advisory lock when the underlying transaction executes `COMMIT` or `ROLLBACK`.
- **`EricksonLopez.Transaction` Interoperability**: Since `ITransactionContext` exposes the `Transaction` property of type `DbTransaction`, handlers can bind transactional locks directly:
  ```csharp
  var lockResult = await context.Transaction.TryAcquireInTransactionAsync("order:" + orderId, logger);
  ```

### 2.2. Declarative `[DistributedLock]` Attribute
- In `EricksonLopez.DistributedLock.Abstractions`, we introduce the attribute:
  ```csharp
  [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
  public sealed class DistributedLockAttribute(string resourceKeyPattern) : Attribute
  {
      public string ResourceKeyPattern { get; } = resourceKeyPattern;
      public int TimeoutSeconds { get; set; } = 0;
      public bool Blocking { get; set; } = false;
  }
  ```
- This attribute allows tagging Mediator commands (e.g. `[DistributedLock("invoices:{BillingCycle}")]`) so that an `EricksonLopez.Mediator` pipeline behavior intercepts execution, resolves the key pattern, acquires the distributed lock, and proceeds to the handler under guaranteed mutual exclusion.

> [!IMPORTANT]
> **Scope Boundary**: The `[DistributedLockAttribute]` is defined in `EricksonLopez.DistributedLock.Abstractions` as a **contract definition only**.
> The `IPipelineBehavior<TRequest, TResponse>` implementation that inspects this attribute, resolves the key pattern,
> and calls `IDistributedLockProvider` is **not included in this repository**.
> It must be implemented in the `dotnet-mediator` pipeline or in the consuming application code.

---

## 3. Consequences

### Positive
- **Zero Cyclical Dependencies**: By building extensions on BCL contracts (`IDbTransaction` / `DbTransaction`), integration works natively with `EricksonLopez.Transaction` without cross-package NuGet dependencies.
- **Ergonomic Cleanliness**: Transaction handlers do not need to manage separate handle lifecycles; the transaction governs lock validity.
- **Declarative Semantics**: Streamlines adopting distributed mutual exclusion in Mediator pipelines declaratively.

---

[← Previous: ADR-004](adr-004-opentelemetry-metrics-and-observability.md) | [Index](README.md)
