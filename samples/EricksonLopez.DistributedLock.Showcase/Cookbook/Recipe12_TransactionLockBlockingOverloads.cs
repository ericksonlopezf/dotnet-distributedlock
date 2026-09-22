// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Showcase.Infrastructure;
using EricksonLopez.DistributedLock.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace EricksonLopez.DistributedLock.Showcase.Cookbook;

/// <summary>
/// Recipe 12: Blocking overloads of TransactionLockExtensions and DbTransaction support.
/// Problem: Need to acquire transactional lock and wait until resource is free,
/// or demonstrate using the DbTransaction overload (instead of IDbTransaction).
/// </summary>
public static class Recipe12_TransactionLockBlockingOverloads
{
    public static async Task RunAsync()
    {
        ConsoleUi.PrintHeader("Recipe 12: Blocking Overloads and DbTransaction in TransactionLockExtensions",
            "AcquireInTransactionAsync (blocking) and DbTransaction overload of SqliteTransactionLockExtensions.");

        var dbPath = Path.Combine(Path.GetTempPath(), "recipe12.db");

        // -- 1. TryAcquireInTransactionAsync(IDbTransaction) -- base overload (already demonstrated in Recipe04)
        ConsoleUi.PrintInfo("1. Verifying TryAcquireInTransactionAsync(IDbTransaction) -- base overload...");
        await using var conn1 = new SqliteConnection($"Data Source={dbPath};");
        await conn1.OpenAsync();
        await using var tx1 = await conn1.BeginTransactionAsync();
        var tryResult = await ((IDbTransaction)tx1).TryAcquireInTransactionAsync("ledger:close:day-1", NullLogger.Instance);
        if (tryResult.IsSuccess)
        {
            await using (tryResult.Value)
            {
                ConsoleUi.PrintSuccess("TryAcquireInTransactionAsync(IDbTransaction) acquired the lock.");
            }
            await tx1.CommitAsync();
            ConsoleUi.PrintSuccess("Transaction confirmed (COMMIT).");
        }
        else
        {
            ConsoleUi.PrintError($"Error: {tryResult.Error.Code}");
        }

        // -- 2. TryAcquireInTransactionAsync(DbTransaction) -- DbTransaction overload (strongly typed base type)
        ConsoleUi.PrintInfo("2. TryAcquireInTransactionAsync(DbTransaction) -- strongly-typed DbTransaction base overload...");
        await using var conn2 = new SqliteConnection($"Data Source={dbPath};");
        await conn2.OpenAsync();
        await using var tx2 = (DbTransaction)(await conn2.BeginTransactionAsync());
        // The DbTransaction overload is inferred automatically from the static DbTransaction type
        var tryDbResult = await tx2.TryAcquireInTransactionAsync("ledger:close:day-2", NullLogger.Instance);
        if (tryDbResult.IsSuccess)
        {
            await using (tryDbResult.Value)
            {
                ConsoleUi.PrintSuccess("TryAcquireInTransactionAsync(DbTransaction) acquired the lock.");
                ConsoleUi.PrintMetric("ResourceId", tryDbResult.Value.ResourceId);
                ConsoleUi.PrintMetric("LockId", tryDbResult.Value.LockId);
            }
            await tx2.CommitAsync();
        }

        // -- 3. AcquireInTransactionAsync(IDbTransaction) -- blocking overload (waits until acquired)
        ConsoleUi.PrintInfo("3. AcquireInTransactionAsync(IDbTransaction) -- blocks until acquired (with CancellationToken)...");
        await using var conn3 = new SqliteConnection($"Data Source={dbPath};");
        await conn3.OpenAsync();
        await using var tx3 = await conn3.BeginTransactionAsync();
        var acquireResult = await ((IDbTransaction)tx3).AcquireInTransactionAsync("ledger:close:day-3", NullLogger.Instance);
        if (acquireResult.IsSuccess)
        {
            await using (acquireResult.Value)
            {
                ConsoleUi.PrintSuccess("AcquireInTransactionAsync(IDbTransaction) acquired lock in blocking mode.");
                ConsoleUi.PrintInfo("  -> This overload retries internally until the resource becomes free.");
            }
            await tx3.CommitAsync();
            ConsoleUi.PrintSuccess("Transaction committed.");
        }

        // -- 4. AcquireInTransactionAsync(DbTransaction) -- blocking overload with DbTransaction
        ConsoleUi.PrintInfo("4. AcquireInTransactionAsync(DbTransaction) -- blocking overload of DbTransaction base type...");
        await using var conn4 = new SqliteConnection($"Data Source={dbPath};");
        await conn4.OpenAsync();
        await using var tx4 = (DbTransaction)(await conn4.BeginTransactionAsync());
        var acquireDbResult = await tx4.AcquireInTransactionAsync("ledger:close:day-4", NullLogger.Instance);
        if (acquireDbResult.IsSuccess)
        {
            await using (acquireDbResult.Value)
            {
                ConsoleUi.PrintSuccess("AcquireInTransactionAsync(DbTransaction) acquired lock successfully.");
            }
            await tx4.CommitAsync();
        }

        ConsoleUi.PrintSuccess("Recipe 12 completed -- all transactional overloads validated.");
        try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { /* Ignore */ }
    }
}
