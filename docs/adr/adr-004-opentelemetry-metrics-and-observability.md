# ADR-004: OpenTelemetry Metrics and Observability

## Status
Accepted

## Date
2026-09-03

**Context**: Production Observability and Zero-Allocation Diagnostics  
**Decision Makers**: Principal Software Architect, Core Infrastructure Team  

---

[← Previous: ADR-003](adr-003-session-lock-keepalive-and-handle-lost-token.md) | [Index](README.md) | [Next: ADR-005 →](adr-005-transaction-and-mediator-ecosystem-integration.md)

---

## 1. Context

In distributed production systems, pessimistic mutual exclusion can become a hidden bottleneck if critical sections take too long or if undiagnosed contention occurs.
Legacy libraries (such as Medallion.Threading) do not provide native OpenTelemetry metrics out-of-the-box, forcing engineers to write manual decorators and ad-hoc telemetry around lock invocations.

We require `EricksonLopez.DistributedLock` to deliver first-class observability with zero external third-party dependencies and strict Native AOT compatibility.

---

## 2. Decision

We implement native **OpenTelemetry** metrics using the standard BCL diagnostic API `System.Diagnostics.Metrics` in the `DistributedLockMetrics` class:

- **Meter Name**: `"EricksonLopez.DistributedLock"`
- **Meter Version**: `"1.0.0"`

### 2.1. Instrument Catalog

| Instrument Name | Type | Unit | Tags | Description |
|---|---|---|---|---|
| `distributed_lock.acquisitions` | Counter (`long`) | `{acquisition}` | `resource_id`, `lock_type`, `status` | Total acquisition attempts classified by outcome (`acquired`, `already_held`, `timeout`, `canceled`, `error`). |
| `distributed_lock.wait_duration` | Histogram (`double`) | `ms` | `resource_id`, `lock_type`, `status` | Elapsed time in milliseconds from attempt initiation to grant or failure/timeout. |
| `distributed_lock.hold_duration` | Histogram (`double`) | `ms` | `resource_id`, `lock_type` | Total duration in milliseconds during which the lock was held prior to calling `DisposeAsync`. |
| `distributed_lock.lost` | Counter (`long`) | `{lock}` | `resource_id`, `lock_id` | Number of active locks unexpectedly lost due to socket or keepalive failures. |

### 2.2. Performance and Trimming Considerations
- Utilizing the standard `System.Diagnostics.Metrics.Meter` API ensures CPU and memory overhead is virtually zero when no listeners (metrics collectors) are registered.
- No third-party packages (e.g. `OpenTelemetry.Api`) are referenced; the library relies on built-in runtime types in .NET 8, 9, and 10, guaranteeing trim-safety and zero reflection.

### 2.3. Host Integration
Consuming applications can register metrics effortlessly via OpenTelemetry .NET:
```csharp
services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("EricksonLopez.DistributedLock")
        .AddPrometheusExporter());
```

---

## 3. Consequences

### Positive
- **Accurate Contention Diagnostics**: Instant visibility in dashboards (Grafana, Prometheus, Datadog) into hot resources via wait duration histograms and `already_held` counters.
- **Hold Duration Visibility**: Exposes slow critical sections that threaten cluster throughput.
- **Proactive Alerting**: The `distributed_lock.lost` counter allows immediate infrastructure alerting if network firewalls or database failovers drop connections.
- **Zero Allocations & Native AOT**: Requires no reflection or heavy intermediate libraries.

---

[← Previous: ADR-003](adr-003-session-lock-keepalive-and-handle-lost-token.md) | [Index](README.md) | [Next: ADR-005 →](adr-005-transaction-and-mediator-ecosystem-integration.md)
