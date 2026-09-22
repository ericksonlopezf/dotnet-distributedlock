// Copyright © Erickson Lopez. MIT License.
using System;
using System.IO;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Showcase.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EricksonLopez.DistributedLock.Showcase.Cookbook;

/// <summary>
/// Recipe 2: Bounded wait with retry and maximum timeout.
/// Problem: An operation can tolerate waiting briefly until another process releases the resource, but must abort if it exceeds a certain threshold.
/// </summary>
public static class Recipe02_BoundedTimeoutRetry
{
    public static async Task RunAsync()
    {
        ConsoleUi.PrintHeader("Recipe 02: Bounded Wait with Maximum Timeout",
            "Usage of TryAcquireAsync(resourceId, timeout) with exponential polling and jitter.");

        var dbPath = Path.Combine(Path.GetTempPath(), "recipe02.db");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSqliteDistributedLock($"Data Source={dbPath};", o =>
        {
            o.RetryInterval = TimeSpan.FromMilliseconds(25);
            o.BackoffJitter = true;
        });
        await using var sp = services.BuildServiceProvider();

        var provider = sp.GetRequiredService<IDistributedLockProvider>();
        const string key = "orders:checkout:user-123";

        // Try to acquire with 500 ms bound
        var result = await provider.TryAcquireAsync(key, TimeSpan.FromMilliseconds(500));
        if (result.IsSuccess)
        {
            await using (result.Value)
            {
                ConsoleUi.PrintSuccess("Lock granted within the allowed timeout interval.");
                await Task.Delay(30);
            }
        }
        else if (result.Error == DistributedLockErrors.Timeout)
        {
            ConsoleUi.PrintWarning("Timeout limit exceeded. Operation could not complete in time.");
        }

        try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { /* Ignore */ }
    }
}
