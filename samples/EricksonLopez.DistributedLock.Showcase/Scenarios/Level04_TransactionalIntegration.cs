// Copyright © Erickson Lopez. MIT License.
using System;
using System.IO;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Showcase.Infrastructure;
using EricksonLopez.DistributedLock.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace EricksonLopez.DistributedLock.Showcase.Scenarios;

/// <summary>
/// Level 4 — Transactional Integration: Locks bound to DbTransaction, safe for PgBouncer and commit boundaries.
/// </summary>
public static class Level04_TransactionalIntegration
{
    public static async Task RunAsync()
    {
        ConsoleUi.PrintHeader("Level 4 — Transactional Integration and Scopes",
            "Direct coupling of lock lifecycle to an active ADO.NET transaction (Commit/Rollback).");

        var dbPath = Path.Combine(Path.GetTempPath(), "showcase_xact.db");
        var connectionString = $"Data Source={dbPath};";

        // Open primary database connection
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync();

        // Start business database transaction
        await using var transaction = await connection.BeginTransactionAsync();
        ConsoleUi.PrintInfo("Database transaction started.");

        const string resourceId = "invoices:generate:INV-2026-001";

        // Acquire lock bound to transaction using extension method
        ConsoleUi.PrintInfo($"Acquiring lock bound to transaction for '{resourceId}'...");
        var lockResult = await transaction.TryAcquireInTransactionAsync(
            resourceId,
            NullLogger.Instance);

        if (lockResult.IsSuccess)
        {
            ConsoleUi.PrintSuccess("Transactional lock acquired inside transaction.");
            var handle = lockResult.Value;

            ConsoleUi.PrintMetric("ResourceId", handle.ResourceId);
            ConsoleUi.PrintMetric("LockId", handle.LockId);

            // Attempt to acquire the same resource again (must detect collision immediately)
            ConsoleUi.PrintInfo("Testing contention on the same protected resource...");
            var secondLockResult = await transaction.TryAcquireInTransactionAsync(
                resourceId,
                NullLogger.Instance);

            if (secondLockResult.IsFailure && secondLockResult.Error == DistributedLockErrors.LockAlreadyHeld)
            {
                ConsoleUi.PrintSuccess("Transactional contention verified: Returned 'DistributedLockErrors.LockAlreadyHeld'.");
            }

            // Perform business operations inside transaction
            ConsoleUi.PrintInfo("Simulating business writes inside transactional scope...");
            await Task.Delay(50);

            // Handle release and transaction commit
            await handle.DisposeAsync();
            await transaction.CommitAsync();
            ConsoleUi.PrintSuccess("Transaction committed. Lock is released for the cluster.");
        }
        else
        {
            ConsoleUi.PrintError($"Failed to acquire transactional lock: {lockResult.Error.Description}");
        }

        try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { /* Ignore */ }
    }
}
