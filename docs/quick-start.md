# Quick Start Guide — EricksonLopez.DistributedLock

> **Get up and running with enterprise distributed locking in .NET in under 5 minutes.**  
> **Version**: 1.0.0 | **Ecosystem**: `EricksonLopez.*` | **Target Runtimes**: .NET 8.0, .NET 9.0, .NET 10.0

---

## 1. Package Installation

Install the core abstractions package along with the dialect provider that matches your storage infrastructure:

```bash
# Core Abstractions and Result Pattern
dotnet add package EricksonLopez.DistributedLock.Abstractions

# Storage Engine Provider (Choose one or more):
dotnet add package EricksonLopez.DistributedLock.PostgreSql
# or dotnet add package EricksonLopez.DistributedLock.SqlServer
# or dotnet add package EricksonLopez.DistributedLock.MySql
# or dotnet add package EricksonLopez.DistributedLock.MariaDb
# or dotnet add package EricksonLopez.DistributedLock.Oracle
# or dotnet add package EricksonLopez.DistributedLock.Sqlite
# or dotnet add package EricksonLopez.DistributedLock.Redis
```

---

## 2. Dependency Injection Setup (`Program.cs`)

Register the provider in your application service collection using the appropriate engine extension method:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// Example A: SQLite (Ideal for single-server, cross-process, or local development)
builder.Services.AddSqliteDistributedLock("Data Source=locks.db;");

// Example B: PostgreSQL Advisory Locks
// builder.Services.AddPostgresDistributedLock(
//     sp => new Npgsql.NpgsqlConnection(builder.Configuration.GetConnectionString("Postgres")));

// Example C: SQL Server Application Locks
// builder.Services.AddSqlServerDistributedLock(
//     builder.Configuration.GetConnectionString("SqlServer")!);

// Example D: Redis Distributed Mutex
// builder.Services.AddRedisDistributedLock(
//     StackExchange.Redis.ConnectionMultiplexer.Connect("localhost:6379"));

var app = builder.Build();
app.Run();
```

---

## 3. Basic Usage Patterns

Inject `IDistributedLockProvider` into your services, workers, or command handlers:

### Pattern A: Declarative Scope Guard (`ExecuteWithLockAsync`) — *Recommended*

The scope guard pattern automatically handles acquisition, executes the critical section, links cancellation tokens with socket severance detection (`HandleLostToken`), and disposes the lock handle upon exit:

```csharp
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.Result;

public sealed class BillingService
{
    private readonly IDistributedLockProvider _lockProvider;

    public BillingService(IDistributedLockProvider lockProvider)
    {
        _lockProvider = lockProvider;
    }

    public async Task<Result<Invoice>> GenerateMonthlyInvoiceAsync(string tenantId, CancellationToken ct)
    {
        return await _lockProvider.ExecuteWithLockAsync(
            $"billing:tenant:{tenantId}",
            async (linkedToken) =>
            {
                // Protected critical section: only one replica executes this at a time
                return await CalculateAndPersistInvoiceAsync(tenantId, linkedToken);
            },
            ct);
    }

    private Task<Invoice> CalculateAndPersistInvoiceAsync(string tenantId, CancellationToken ct) =>
        Task.FromResult(new Invoice { TenantId = tenantId });
}

public sealed class Invoice
{
    public string TenantId { get; set; } = string.Empty;
}
```

### Pattern B: Non-Blocking Immediate Attempt (`TryAcquireAsync`)

Ideal for background cleanup jobs or scheduled reconciliations where a contested lock simply means another worker is already processing the task:

```csharp
public async Task RunHourlyCleanupAsync(CancellationToken ct)
{
    var result = await _lockProvider.TryAcquireAsync("jobs:cleanup:audit-logs", ct);
    if (result.IsFailure)
    {
        // Lock already held by another container/worker replica
        Console.WriteLine($"Cleanup skipped: {result.Error.Description}");
        return;
    }

    await using (result.Value)
    {
        await PurgeExpiredAuditLogsAsync(ct);
    } // Lock released deterministically here
}
```

### Pattern C: Bounded Waiting with Jittered Backoff

When a service is willing to wait up to a specified deadline for an ongoing task to complete:

```csharp
public async Task<Result<bool>> ProcessPaymentAsync(string paymentId, CancellationToken ct)
{
    var timeout = TimeSpan.FromSeconds(5);
    var result = await _lockProvider.TryAcquireAsync($"payments:{paymentId}", timeout, ct);

    if (result.IsFailure)
    {
        return Result<bool>.Failure(result.Error);
    }

    await using (result.Value)
    {
        await ExecutePaymentGatewayCallAsync(paymentId, ct);
        return Result<bool>.Success(true);
    }
}
```

---

## 4. Next Steps

- Explore production patterns in the [Cookbook](cookbook.md).
- Review operational recommendations in [Best Practices](best-practices.md).
- Examine detailed signatures in the [API Reference](api-reference.md).
- Run the executable scenarios in the [Showcase Sample](showcase.md).
