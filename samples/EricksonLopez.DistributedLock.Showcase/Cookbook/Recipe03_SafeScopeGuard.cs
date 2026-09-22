// Copyright © Erickson Lopez. MIT License.
using System;
using System.IO;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Showcase.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EricksonLopez.DistributedLock.Showcase.Cookbook;

/// <summary>
/// Recipe 3: Declarative scope guard with ExecuteWithLockAsync.
/// Problem: You want to ensure that the handle is always released and automatically link cancellation without writing repetitive 'await using'.
/// </summary>
public static class Recipe03_SafeScopeGuard
{
    public static async Task RunAsync()
    {
        ConsoleUi.PrintHeader("Recipe 03: Declarative Scope Guard with ExecuteWithLockAsync",
            "Safe execution with guaranteed release and CancellationToken linking.");

        var dbPath = Path.Combine(Path.GetTempPath(), "recipe03.db");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSqliteDistributedLock($"Data Source={dbPath};");
        await using var sp = services.BuildServiceProvider();

        var provider = sp.GetRequiredService<IDistributedLockProvider>();

        // Execute action returning a typed business value
        var result = await provider.ExecuteWithLockAsync("billing:calculate:tenant-99", async (ct) =>
        {
            ConsoleUi.PrintInfo("Computing consolidated billing inside the lock...");
            await Task.Delay(40, ct);
            return 1450.75m;
        });

        if (result.IsSuccess)
        {
            ConsoleUi.PrintSuccess($"Calculation completed successfully. Total: ${result.Value}");
        }
        else
        {
            ConsoleUi.PrintError($"Scope guard error: {result.Error.Description}");
        }

        try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { /* Ignore */ }
    }
}
