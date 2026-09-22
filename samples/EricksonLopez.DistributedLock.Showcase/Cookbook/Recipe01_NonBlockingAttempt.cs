// Copyright © Erickson Lopez. MIT License.
using System;
using System.IO;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Showcase.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EricksonLopez.DistributedLock.Showcase.Cookbook;

/// <summary>
/// Recipe 1: Immediate non-blocking acquisition attempt.
/// Problem: You want to attempt to acquire a lock and, if busy, continue immediately without waiting or blocking threads.
/// </summary>
public static class Recipe01_NonBlockingAttempt
{
    public static async Task RunAsync()
    {
        ConsoleUi.PrintHeader("Recipe 01: Immediate Non-Blocking Acquisition",
            "Usage of TryAcquireAsync(resourceId) for immediate bail-out if the lock is held.");

        var dbPath = Path.Combine(Path.GetTempPath(), "recipe01.db");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSqliteDistributedLock($"Data Source={dbPath};");
        await using var sp = services.BuildServiceProvider();

        var provider = sp.GetRequiredService<IDistributedLockProvider>();
        const string key = "jobs:cleanup-cache";

        // Immediate acquisition
        var result = await provider.TryAcquireAsync(key);
        if (result.IsSuccess)
        {
            await using (result.Value)
            {
                ConsoleUi.PrintSuccess("Lock acquired immediately. Executing cleanup...");
                await Task.Delay(20);
            }
        }
        else if (result.Error == DistributedLockErrors.LockAlreadyHeld)
        {
            ConsoleUi.PrintWarning("Job is already being executed by another node. Skipping without waiting.");
        }

        try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { /* Ignore */ }
    }
}
