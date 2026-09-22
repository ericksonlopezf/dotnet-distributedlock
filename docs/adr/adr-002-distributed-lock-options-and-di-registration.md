# ADR-002: Distributed Lock Options and DI Registration

## Status
Accepted

## Date
2026-09-03

**Context**: Developer Experience, Configuration, and Thundering Herd Prevention  
**Decision Makers**: Principal Software Architect, Core Infrastructure Team  

---

[← Previous: ADR-001](adr-001-distributed-lock-abstraction.md) | [Index](README.md) | [Next: ADR-003 →](adr-003-session-lock-keepalive-and-handle-lost-token.md)

---

## 1. Context

In the initial implementation of `PostgresDistributedLockProvider`:
1. Registering with the Dependency Injection container (`IServiceCollection`) required verbose manual factories and direct provider instantiation in application startup code.
2. The retry interval in acquisitions with timeout (`TryAcquireAsync(resourceId, timeout)`) was hardcoded to a static 50ms without pseudo-random jitter. Under contention across concurrent workers (e.g. 10 replicas awaiting migration job completion), this uniform interval triggered the **thundering herd** phenomenon: all nodes awoke simultaneously, flooding PostgreSQL with concurrent `pg_try_advisory_lock` queries.
3. No standardized pattern existed to configure command timeouts (`CommandTimeout`) or keepalive cadence.

---

## 2. Decision

### 2.1. Strongly Typed Configuration Object (`PostgresLockOptions`)
We introduce `PostgresLockOptions` with the following configurable parameters:
- `KeepaliveCadence`: Interval for connection health ping checks (default: 30 seconds; disabled when `<= TimeSpan.Zero`).
- `InitialPollingInterval`: Initial wait interval between attempts under contention (default: 25ms).
- `MaxPollingInterval`: Upper bound for exponential backoff (default: 250ms).
- `JitterRatio`: Pseudo-random dispersion ratio (between 0.0 and 1.0, default: 0.25).
- `CommandTimeoutSeconds`: Timeout in seconds for individual SQL commands.

### 2.2. Polling Algorithm with Exponential Backoff and Jitter
To mitigate thundering herd spikes, the delay between successive polls is calculated as:
$$\text{delay} = \max\left(1.0, \text{interval} \times \left(1.0 + (2 \cdot U - 1) \cdot \text{ratio}\right)\right)$$
where $U \sim \text{Uniform}(0, 1)$ is sampled from `Random.Shared`, and the base interval grows geometrically after each unsuccessful iteration:
$$\text{interval}_{k+1} = \min(\text{interval}_k \times 1.5, \text{MaxPollingInterval})$$
This distributes polling queries evenly across the time window.

### 2.3. Idiomatic DI Registration Extensions
Extension methods are provided under the standard `Microsoft.Extensions.DependencyInjection` namespace:
```csharp
services.AddPostgresDistributedLock(
    sp => new NpgsqlConnection(connectionString),
    options =>
    {
        options.KeepaliveCadence = TimeSpan.FromSeconds(20);
        options.JitterRatio = 0.3;
    });
```
The registration binds `IDistributedLockProvider` as `Singleton`, resolving `ILogger<PostgresDistributedLockProvider>` and configured options via `IOptions<PostgresLockOptions>`.

---

## 3. Consequences

### Positive
- **Contention Mitigation**: Backoff with jitter prevents CPU and socket saturation spikes on PostgreSQL when multiple workers compete for the same resource.
- **Unified Configuration**: Fully conforms to the `IOptions<TOptions>` pattern in ASP.NET Core and .NET Generic Host.
- **Boilerplate Reduction**: Configuration in `Program.cs` is reduced to a single idiomatic extension call.

---

[← Previous: ADR-001](adr-001-distributed-lock-abstraction.md) | [Index](README.md) | [Next: ADR-003 →](adr-003-session-lock-keepalive-and-handle-lost-token.md)
