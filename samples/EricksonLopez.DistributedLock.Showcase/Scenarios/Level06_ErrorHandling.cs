// Copyright © Erickson Lopez. MIT License.
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Showcase.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EricksonLopez.DistributedLock.Showcase.Scenarios;

/// <summary>
/// Level 6 — ROP Error Handling: Standardized error classification without exceptions and functional resilience.
/// </summary>
public static class Level06_ErrorHandling
{
    public static async Task RunAsync()
    {
        ConsoleUi.PrintHeader("Level 6 — ROP Error Handling",
            "Typed error processing via Result<T> and the DistributedLockErrors catalog.");

        var dbPath = Path.Combine(Path.GetTempPath(), "showcase_errors.db");
        var connectionString = $"Data Source={dbPath};";

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSqliteDistributedLock(connectionString);

        await using var sp = services.BuildServiceProvider();
        var lockProvider = sp.GetRequiredService<IDistributedLockProvider>();

        const string resource = "errors:demo:resource";

        // ERROR 1: LockAlreadyHeld (Immediate acquisition rejected due to prior occupancy)
        ConsoleUi.PrintInfo("1. Triggering 'DistributedLockErrors.LockAlreadyHeld'...");
        var primaryHandle = await lockProvider.TryAcquireAsync(resource);
        if (primaryHandle.IsSuccess)
        {
            await using (primaryHandle.Value)
            {
                var secondaryAttempt = await lockProvider.TryAcquireAsync(resource);
                InspectError(secondaryAttempt.Error, DistributedLockErrors.LockAlreadyHeld);
            }
        }

        // ERROR 2: Timeout (Expiration of maximum wait timeout under contention)
        ConsoleUi.PrintInfo("2. Triggering 'DistributedLockErrors.Timeout'...");
        var blockerHandle = await lockProvider.TryAcquireAsync(resource);
        if (blockerHandle.IsSuccess)
        {
            await using (blockerHandle.Value)
            {
                var timeoutAttempt = await lockProvider.TryAcquireAsync(resource, TimeSpan.FromMilliseconds(50));
                InspectError(timeoutAttempt.Error, DistributedLockErrors.Timeout);
            }
        }

        // ERROR 3: Canceled (Voluntary cancellation via CancellationToken)
        ConsoleUi.PrintInfo("3. Triggering 'DistributedLockErrors.Canceled'...");
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancelled immediately
        var canceledAttempt = await lockProvider.TryAcquireAsync(resource, cts.Token);
        InspectError(canceledAttempt.Error, DistributedLockErrors.Canceled);

        // ERROR 4: LockLost (Representation of lost session or socket)
        ConsoleUi.PrintInfo("4. Inspeccionando 'DistributedLockErrors.LockLost'...");
        ConsoleUi.PrintMetric("Code", DistributedLockErrors.LockLost.Code);
        ConsoleUi.PrintMetric("Description", DistributedLockErrors.LockLost.Description);

        try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { /* Ignore */ }
    }

    private static void InspectError(EricksonLopez.Result.Error actualError, EricksonLopez.Result.Error expectedError)
    {
        if (actualError == expectedError)
        {
            ConsoleUi.PrintSuccess($"Coincidencia exacta: '{actualError.Code}' == '{expectedError.Code}'");
            ConsoleUi.PrintInfo($"Description: {actualError.Description}");
        }
        else
        {
            ConsoleUi.PrintError($"Error mismatch: Expected '{expectedError.Code}', received '{actualError.Code}'.");
        }
    }
}
