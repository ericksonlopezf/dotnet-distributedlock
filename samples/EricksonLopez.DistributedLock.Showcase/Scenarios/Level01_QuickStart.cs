// Copyright © Erickson Lopez. MIT License.
using System;
using System.IO;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Showcase.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EricksonLopez.DistributedLock.Showcase.Scenarios;

/// <summary>
/// Level 1 — Quick Start: Minimal configuration, DI registration, and first functional lock/release lifecycle.
/// </summary>
public static class Level01_QuickStart
{
    public static async Task RunAsync()
    {
        ConsoleUi.PrintHeader("Level 1 — Quick Start", "Minimal configuration, Dependency Injection, and baseline mutual exclusion lifecycle.");

        var dbPath = Path.Combine(Path.GetTempPath(), "showcase_quickstart.db");
        var connectionString = $"Data Source={dbPath};";

        // 1. Configure Dependency Injection container
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

        // Register SQLite lock provider via official extension
        services.AddSqliteDistributedLock(connectionString);

        await using var serviceProvider = services.BuildServiceProvider();

        // 2. Resolve IDistributedLockProvider
        var lockProvider = serviceProvider.GetRequiredService<IDistributedLockProvider>();
        ConsoleUi.PrintInfo($"Resolved provider: {lockProvider.GetType().Name}");

        const string resourceKey = "orders:process:1001";

        // 3. First acquisition attempt (Successful)
        ConsoleUi.PrintInfo($"Attempting to acquire lock for '{resourceKey}'...");
        var acquisitionResult = await lockProvider.TryAcquireAsync(resourceKey);

        if (acquisitionResult.IsSuccess)
        {
            ConsoleUi.PrintSuccess("Lock acquired successfully.");

            // Retain handle inside an 'await using' scope
            await using (acquisitionResult.Value)
            {
                ConsoleUi.PrintInfo("Inside protected critical section.");

                // 4. Attempt to acquire the same resource concurrently from another consumer
                ConsoleUi.PrintInfo("Simulating concurrent collision on the same resource...");
                var collisionResult = await lockProvider.TryAcquireAsync(resourceKey);

                if (collisionResult.IsFailure)
                {
                    ConsoleUi.PrintWarning($"Collision correctly detected. Error: {collisionResult.Error.Code} - {collisionResult.Error.Description}");
                    if (collisionResult.Error == DistributedLockErrors.LockAlreadyHeld)
                    {
                        ConsoleUi.PrintSuccess("ROP Verification: Error matches 'DistributedLockErrors.LockAlreadyHeld' exactly.");
                    }
                }
                else
                {
                    ConsoleUi.PrintError("ERROR: Mutual exclusion violated.");
                }
            }

            ConsoleUi.PrintInfo("Handle released (DisposeAsync completed).");

            // 5. Attempt to acquire again once released
            var reacquireResult = await lockProvider.TryAcquireAsync(resourceKey);
            if (reacquireResult.IsSuccess)
            {
                await using (reacquireResult.Value)
                {
                    ConsoleUi.PrintSuccess("Re-acquisition successful following prior release.");
                }
            }
        }
        else
        {
            ConsoleUi.PrintError($"Unexpected failure acquiring initial lock: {acquisitionResult.Error.Description}");
        }

        // Cleanup demonstration database file
        try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { /* Ignore */ }
    }
}
