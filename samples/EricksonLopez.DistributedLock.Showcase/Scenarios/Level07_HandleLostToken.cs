// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Showcase.Infrastructure;

namespace EricksonLopez.DistributedLock.Showcase.Scenarios;

/// <summary>
/// Level 7 — Lock Loss Detection: Using IDistributedLockHandle.HandleLostToken to abort long-running tasks on network drop.
/// Also demonstrates AcquireHandleAsync (unbounded blocking) and AcquireHandleAsync(timeout).
/// </summary>
public static class Level07_HandleLostToken
{
    public static async Task RunAsync()
    {
        ConsoleUi.PrintHeader("Level 7 — Lock Loss Detection (HandleLostToken)",
            "Zombie write mitigation via proactive cancellation upon disconnection or failover.");

        // ── PART A: Demonstration of real lock loss path (SimulateLockLoss) ──────────────────────
        ConsoleUi.PrintInfo("PART A: Simulating actual lock loss during batch processing...");

        var provider = new InMemoryDistributedLockProvider();
        const string resourceId = "batch:payroll:generate:2026-09";

        ConsoleUi.PrintInfo($"Acquiring strongly-typed handle with 'TryAcquireHandleAsync' for '{resourceId}'...");
        var acquireResult = await provider.TryAcquireHandleAsync(resourceId);

        if (acquireResult.IsSuccess)
        {
            var handle = acquireResult.Value;
            await using (handle)
            {
                ConsoleUi.PrintSuccess("Handle acquired successfully.");
                ConsoleUi.PrintMetric("ResourceId", handle.ResourceId);
                ConsoleUi.PrintMetric("LockId (Hash)", handle.LockId);

                using var userCts = new CancellationTokenSource();
                // Linking user token with lock loss token
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    userCts.Token,
                    handle.HandleLostToken);

                ConsoleUi.PrintInfo("Starting long-running background processing monitoring 'HandleLostToken'...");

                var workerTask = Task.Run(async () =>
                {
                    try
                    {
                        for (var step = 1; step <= 10; step++)
                        {
                            // Proactive check on each work loop iteration
                            linkedCts.Token.ThrowIfCancellationRequested();
                            ConsoleUi.PrintInfo($"  Worker: Processing stage {step}/10...");
                            await Task.Delay(40, linkedCts.Token);
                        }
                        ConsoleUi.PrintSuccess("Worker completed full batch without incident.");
                    }
                    catch (OperationCanceledException) when (handle.HandleLostToken.IsCancellationRequested)
                    {
                        // This block MUST execute when provider detects session drop
                        ConsoleUi.PrintWarning("CRITICAL ALERT! Lock ownership loss detected (HandleLostToken triggered).");
                        ConsoleUi.PrintWarning("Worker safely aborted to prevent corrupted zombie writes.");
                        DistributedLockMetrics.RecordLockLost(handle.ResourceId, handle.LockId);
                    }
                });

                // Simulate lock interruption mid-execution (4 steps completed × 40ms = ~160ms)
                await Task.Delay(170);
                ConsoleUi.PrintInfo("Simulating abrupt database connection drop...");

                // In a production provider (Npgsql, SqlClient, Redis), this occurs automatically
                // when the keepalive loop detects socket termination.
                // InMemoryDistributedLockProvider exposes SimulateLockLoss() for testing and demonstrations.
                if (handle is InMemoryDistributedLockProvider.InMemoryLockHandle inMemoryHandle)
                {
                    inMemoryHandle.SimulateLockLoss();
                    ConsoleUi.PrintInfo("→ HandleLostToken triggered. Worker will detect signal on next iteration.");
                }

                await workerTask;
            }

            ConsoleUi.PrintSuccess("PART A completed: Lock loss path correctly exercised.");
        }
        else
        {
            ConsoleUi.PrintError($"Failed to acquire handle: {acquireResult.Error.Description}");
        }

        // ── PART B: AcquireHandleAsync — blocking until acquired ────────────────────────────────
        ConsoleUi.PrintInfo("");
        ConsoleUi.PrintInfo("PART B: Demonstrating 'AcquireHandleAsync' (unbounded blocking until acquired)...");

        var provider2 = new InMemoryDistributedLockProvider();
        const string blockResource = "batch:payroll:monthly:report";

        // AcquireHandleAsync: blocks indefinitely until the lock is free.
        // In InMemoryDistributedLockProvider, if no one else holds the lock, returns immediately.
        var blockingResult = await provider2.AcquireHandleAsync(blockResource);
        if (blockingResult.IsSuccess)
        {
            var handle = blockingResult.Value;
            await using (handle)
            {
                ConsoleUi.PrintSuccess($"AcquireHandleAsync acquired handle for '{handle.ResourceId}'.");
                ConsoleUi.PrintMetric("LockId", handle.LockId);
                ConsoleUi.PrintMetric("FencingToken", handle.FencingToken?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null (not supported by engine)");
                await Task.Delay(10);
            }
            ConsoleUi.PrintSuccess("Handle released via DisposeAsync().");
        }

        // ── PART C: AcquireHandleAsync with timeout ──────────────────────────────────────────────
        ConsoleUi.PrintInfo("");
        ConsoleUi.PrintInfo("PART C: Demonstrating 'AcquireHandleAsync(resourceId, timeout)' (bounded blocking)...");

        var provider3 = new InMemoryDistributedLockProvider();

        // Retain the lock first with another consumer
        var firstHandle = await provider3.TryAcquireHandleAsync(blockResource);
        if (firstHandle.IsSuccess)
        {
            await using var _ = firstHandle.Value;

            // Attempt AcquireHandleAsync with timeout: will fail because lock is held
            var timedResult = await provider3.AcquireHandleAsync(blockResource, TimeSpan.FromMilliseconds(100));
            if (timedResult.IsFailure)
            {
                ConsoleUi.PrintSuccess($"AcquireHandleAsync(timeout=100ms) returned correctly: {timedResult.Error.Code}");
                ConsoleUi.PrintInfo($"  → Lock was held by first consumer. Second consumer waited 100ms and yielded.");
            }
        }

        ConsoleUi.PrintSuccess("Level 7 completed — all handle overloads and lock loss paths validated.");
    }
}
