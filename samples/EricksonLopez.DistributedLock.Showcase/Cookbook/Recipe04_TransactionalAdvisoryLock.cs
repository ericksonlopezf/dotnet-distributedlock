// Copyright © Erickson Lopez. MIT License.
using System;
using System.IO;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Showcase.Infrastructure;
using EricksonLopez.DistributedLock.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace EricksonLopez.DistributedLock.Showcase.Cookbook;

/// <summary>
/// Recipe 4: Locks bound to database transaction lifecycle.
/// Problem: In transactional architectures or environments with connection poolers (PgBouncer), lock release must coincide with COMMIT or ROLLBACK.
/// </summary>
public static class Recipe04_TransactionalAdvisoryLock
{
    public static async Task RunAsync()
    {
        ConsoleUi.PrintHeader("Recipe 04: Database Transaction-Bound Lock",
            "Usage of TryAcquireInTransactionAsync on active DbTransaction.");

        var dbPath = Path.Combine(Path.GetTempPath(), "recipe04.db");
        await using var connection = new SqliteConnection($"Data Source={dbPath};");
        await connection.OpenAsync();

        await using var tx = await connection.BeginTransactionAsync();
        ConsoleUi.PrintInfo("Transaction opened.");

        var lockResult = await tx.TryAcquireInTransactionAsync("financial:settlement:day-end", NullLogger.Instance);
        if (lockResult.IsSuccess)
        {
            await using (lockResult.Value)
            {
                ConsoleUi.PrintSuccess("Transactional lock acquired. Processing cash close...");
                await Task.Delay(30);
            }

            await tx.CommitAsync();
            ConsoleUi.PrintSuccess("Transaction committed (COMMIT) and lock released.");
        }

        try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { /* Ignore */ }
    }
}
