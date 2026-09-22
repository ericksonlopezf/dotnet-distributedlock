// Copyright © Erickson Lopez. MIT License.
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Showcase.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EricksonLopez.DistributedLock.Showcase.Cookbook;

/// <summary>
/// Recipe 11: Blocking acquisition overloads (AcquireAsync).
/// Problem: You need the operation to wait indefinitely until the lock becomes available,
/// or wait until a bounded timeout expires, without returning immediately.
/// Solution: Use AcquireAsync/AcquireHandleAsync instead of TryAcquireAsync.
/// </summary>
public static class Recipe11_BlockingAcquireOverloads
{
    public static async Task RunAsync()
    {
        ConsoleUi.PrintHeader("Recipe 11: Blocking Overloads of IDistributedLockProvider",
            "AcquireAsync (unbounded), AcquireAsync (timeout), AcquireHandleAsync (unbounded), AcquireHandleAsync (timeout).");

        var dbPath = Path.Combine(Path.GetTempPath(), "recipe11.db");
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSqliteDistributedLock($"Data Source={dbPath};");
        await using var sp = services.BuildServiceProvider();

        var provider = sp.GetRequiredService<IDistributedLockProvider>();
        const string key = "jobs:monthly-report:run";

        // -- 1. AcquireAsync(string, CancellationToken) -- wait until acquired (unbounded)
        ConsoleUi.PrintInfo("1. AcquireAsync(resourceId, ct) -- blocks until acquired (infinite wait)...");
        var acquireResult = await provider.AcquireAsync(key);
        if (acquireResult.IsSuccess)
        {
            await using (acquireResult.Value)
            {
                ConsoleUi.PrintSuccess("AcquireAsync acquired lock without timeout.");
                await Task.Delay(20);
            }
            ConsoleUi.PrintSuccess("Lock released via DisposeAsync().");
        }
        else
        {
            // AcquireAsync only fails if the CancellationToken is cancelled
            ConsoleUi.PrintWarning($"AcquireAsync cancelled: {acquireResult.Error.Code}");
        }

        // -- 2. AcquireAsync(string, TimeSpan, CancellationToken) -- blocking with maximum timeout
        ConsoleUi.PrintInfo("2. AcquireAsync(resourceId, timeout, ct) -- blocks until acquired or timeout expires...");
        var retainerResult = await provider.TryAcquireAsync(key);
        if (retainerResult.IsSuccess)
        {
            var blockedAttempt = await provider.AcquireAsync(key, TimeSpan.FromMilliseconds(100));
            if (blockedAttempt.IsFailure)
            {
                ConsoleUi.PrintSuccess($"AcquireAsync(timeout=100ms) returned correctly: {blockedAttempt.Error.Code}");
                ConsoleUi.PrintInfo("  -> Lock was held, waited 100ms and returned without exception.");
            }
            await retainerResult.Value.DisposeAsync();
        }

        // -- 3. AcquireHandleAsync(string, CancellationToken) -- blocking handle without timeout
        ConsoleUi.PrintInfo("3. AcquireHandleAsync(resourceId, ct) -- acquires IDistributedLockHandle in blocking mode...");
        var handleResult = await provider.AcquireHandleAsync(key);
        if (handleResult.IsSuccess)
        {
            var handle = handleResult.Value;
            await using (handle)
            {
                ConsoleUi.PrintSuccess($"AcquireHandleAsync acquired handle for '{handle.ResourceId}'.");
                ConsoleUi.PrintMetric("ResourceId", handle.ResourceId);
                ConsoleUi.PrintMetric("LockId", handle.LockId);
                ConsoleUi.PrintMetric("FencingToken", handle.FencingToken?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null");
                await Task.Delay(20);
            }
            ConsoleUi.PrintSuccess("Handle released via DisposeAsync().");
        }

        // -- 4. AcquireHandleAsync(string, TimeSpan, CancellationToken) -- handle with timeout
        ConsoleUi.PrintInfo("4. AcquireHandleAsync(resourceId, timeout, ct) -- acquires handle or timeout expires...");
        var retainerHandle = await provider.TryAcquireAsync(key);
        if (retainerHandle.IsSuccess)
        {
            var timedHandleResult = await provider.AcquireHandleAsync(key, TimeSpan.FromMilliseconds(80));
            if (timedHandleResult.IsFailure)
            {
                ConsoleUi.PrintSuccess($"AcquireHandleAsync(timeout=80ms) returned: {timedHandleResult.Error.Code}");
            }
            await retainerHandle.Value.DisposeAsync();
        }

        // -- 5. ExecuteWithLockAsync(string, TimeSpan, Func<ct, Task>) -- third overload
        ConsoleUi.PrintInfo("5. ExecuteWithLockAsync(resourceId, timeout, action) -- scope guard with bounded timeout...");
        var scopeResult = await provider.ExecuteWithLockAsync(
            key,
            TimeSpan.FromSeconds(2),
            async (ct) =>
            {
                ConsoleUi.PrintInfo("  -> Inside protected scope guard with maximum 2s timeout.");
                await Task.Delay(30, ct);
            });

        if (scopeResult.IsSuccess)
        {
            ConsoleUi.PrintSuccess("ExecuteWithLockAsync(resourceId, timeout, action) completed successfully.");
        }

        ConsoleUi.PrintSuccess("Recipe 11 completed -- all blocking overloads validated.");
        try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { /* Ignore */ }
    }
}
