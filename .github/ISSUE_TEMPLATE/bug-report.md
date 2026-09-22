---
name: Bug Report
about: Create a report to help reproduce and fix an issue in EricksonLopez.DistributedLock
title: "[BUG] "
labels: ["bug"]
assignees: "ericksonlopezf"
---

### Affected Package & Version
<!-- Example: EricksonLopez.DistributedLock.PostgreSql v1.0.0 -->
- **Package**:
- **Version**:
- **Target Framework**: [.NET 8.0 / .NET 9.0 / .NET 10.0]
- **Operating System**: [Linux / Windows / macOS]

### Underlying Storage Engine & Infrastructure
- **Database Engine**: [PostgreSQL / SQL Server / MySQL / MariaDB / Oracle / SQLite / Redis]
- **Engine Version**:
- **Connection Pooler / Proxy**: [None / PgBouncer / ProxySQL / Envoy]
- **Pool Mode (if applicable)**: [Session / Transaction / Statement]

### Describe the Bug
<!-- A clear and concise description of what the bug is. -->

### Reproduction Steps
<!-- Steps to reproduce the behavior: -->
1. Configure `IDistributedLockProvider` using `...`
2. Attempt to acquire lock on key `...`
3. Notice behavior `...`

### Expected vs. Actual Behavior
- **Expected**:
- **Actual**:

### Minimal Reproduction Code
```csharp
// Paste minimal C# reproduction snippet here
```

### Additional Context & Logs
<!-- Add any relevant application logs, stack traces, or metrics here. -->
