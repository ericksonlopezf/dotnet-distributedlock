// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Showcase.Infrastructure;

namespace EricksonLopez.DistributedLock.Showcase.Cookbook;

/// <summary>
/// Recipe 10: Cancellation and graceful shutdown handling.
/// Problem: If application receives SIGTERM or CancellationToken cancels while waiting or holding a lock, guarantee immediate leak-free release.
/// </summary>
public static class Recipe10_HandlingCancellationAndShutdown
{
    public static async Task RunAsync()
    {
        ConsoleUi.PrintHeader("Recipe 10: Cancellation and Graceful Shutdown",
            "Proper cancellation signal propagation in blocking calls and scope guards.");

        var provider = new InMemoryDistributedLockProvider();
        using var shutdownCts = new CancellationTokenSource();

        // Simulate shutdown signal after 50 ms
        shutdownCts.CancelAfter(50);

        ConsoleUi.PrintInfo("Starting blocking acquisition attempt with active shutdown token...");
        var acquireResult = await provider.AcquireAsync("shutdown:test:resource", shutdownCts.Token);

        if (acquireResult.IsFailure)
        {
            if (acquireResult.Error == DistributedLockErrors.Canceled)
            {
                ConsoleUi.PrintSuccess("Cancellation handled cleanly: Returned 'DistributedLockErrors.Canceled' without exceptions.");
            }
            else
            {
                ConsoleUi.PrintWarning($"Result received: {acquireResult.Error.Code}");
            }
        }
        else
        {
            await using (acquireResult.Value)
            {
                ConsoleUi.PrintInfo("Lock acquired before the signal.");
            }
        }
    }
}
