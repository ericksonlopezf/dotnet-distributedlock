// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Showcase.Infrastructure;

namespace EricksonLopez.DistributedLock.Showcase.Scenarios;

/// <summary>
/// Level 0 — Conceptual: Core principles of distributed coordination and mutual exclusion.
/// </summary>
public static class Level00_Conceptual
{
    public static Task RunAsync()
    {
        ConsoleUi.PrintHeader("Level 0 — Conceptual: Tier 0 Distributed Coordination Architecture",
            "What is EricksonLopez.DistributedLock, what problems does it solve, and how does it compare to alternatives?");

        Console.WriteLine("""
        1. THE FUNDAMENTAL PROBLEM
           In modern distributed applications (multiple Kubernetes pods, serverless workers, or multi-instance microservices),
           in-process synchronization primitives like 'lock(obj)', 'SemaphoreSlim', or 'Monitor' fail
           inevitably because their state resides solely in the memory of a single .NET CLR runtime.
           If two instances concurrently execute a critical section (e.g., billing generation, account debits,
           or inventory synchronization), concurrency hazards like 'double spending', 'lost updates', or data corruption occur.

        2. THE SOLUTION: ERICKSONLOPEZ.DISTRIBUTEDLOCK
           Provides a unified Tier 0 abstraction ('IDistributedLockProvider') guaranteeing strict mutual exclusion
           across network boundaries, leveraging your existing enterprise infrastructure:
           • PostgreSQL: Native Advisory Locks ('pg_advisory_lock', 'pg_try_advisory_xact_lock')
           • SQL Server: Native Application Locks ('sys.sp_getapplock', 'sys.sp_releaseapplock')
           • MySQL & MariaDB: User-Level Locks ('GET_LOCK', 'RELEASE_LOCK')
           • Oracle: DBMS_LOCK ('DBMS_LOCK.REQUEST', 'DBMS_LOCK.RELEASE')
           • SQLite: Table-enforced Mutex with Lease TTL and Monotonic Fencing
           • Redis: Atomic 'SET NX PX' with Heartbeat Renewal and Lua Script release

        3. KEY ARCHITECTURAL DIFFERENTIATORS
           ✔ Railway-Oriented Programming (ROP): Never throws exceptions on contention; returns 'Result<IDistributedLockHandle>'.
           ✔ Native AOT & Trimming Friendly: 100% compatible with Ahead-of-Time compilation with zero dynamic reflection.
           ✔ Loss Detection ('HandleLostToken'): Notifies background workers immediately if underlying transport drops.
           ✔ Monotonic Fencing Tokens: Mitigates split-brain and GC pause (STW GC) race conditions in downstream storage.
           ✔ Transaction-Bound Locks: Native support for binding lock lifecycles to 'DbTransaction' commit/rollback boundaries.
           ✔ Native OpenTelemetry: Standardized metrics via BCL 'System.Diagnostics.Metrics' (acquisitions, hold duration, wait duration).
        """);

        ConsoleUi.PrintSuccess("Conceptual foundations presented successfully.");
        return Task.CompletedTask;
    }
}
