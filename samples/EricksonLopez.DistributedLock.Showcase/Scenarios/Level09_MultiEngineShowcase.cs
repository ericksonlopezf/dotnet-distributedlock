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
/// Level 9 — Multi-Engine Matrix: Absolute portability demonstration across all 7 official engines.
/// </summary>
public static class Level09_MultiEngineShowcase
{
    public static async Task RunAsync()
    {
        ConsoleUi.PrintHeader("Level 9 — Multi-Engine Matrix",
            "Total isolation between business logic and underlying infrastructure dialects.");

        Console.WriteLine("""
        OFFICIALLY SUPPORTED PROVIDER MATRIX:
        ---------------------------------------------------------------------------------------------
        Engine          NuGet Package                               Lock Primitive
        ---------------------------------------------------------------------------------------------
        PostgreSQL      EricksonLopez.DistributedLock.PostgreSql     pg_advisory_lock / pg_try_advisory_xact_lock
        SQL Server      EricksonLopez.DistributedLock.SqlServer      sys.sp_getapplock / sys.sp_releaseapplock
        MySQL           EricksonLopez.DistributedLock.MySql          GET_LOCK / RELEASE_LOCK
        MariaDB         EricksonLopez.DistributedLock.MariaDb        GET_LOCK / RELEASE_LOCK
        Oracle          EricksonLopez.DistributedLock.Oracle         DBMS_LOCK.REQUEST / DBMS_LOCK.RELEASE
        SQLite          EricksonLopez.DistributedLock.Sqlite         __distributed_locks table + TTL lease
        Redis           EricksonLopez.DistributedLock.Redis          SET NX PX + Lua release script
        ---------------------------------------------------------------------------------------------
        """);

        ConsoleUi.PrintInfo("Demonstrating the Golden Rule: Consuming code relies on IDistributedLockProvider and never changes:");

        // Demonstration with active local SQLite
        var dbPath = Path.Combine(Path.GetTempPath(), "showcase_matrix.db");
        var connectionString = $"Data Source={dbPath};";

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSqliteDistributedLock(connectionString);

        await using var sp = services.BuildServiceProvider();
        var lockProvider = sp.GetRequiredService<IDistributedLockProvider>();

        // Execute dialect-agnostic service
        await ExecuteBusinessServiceAgnosticAsync(lockProvider, "report:monthly:financial");

        ConsoleUi.PrintSuccess("The same method 'ExecuteBusinessServiceAgnosticAsync' seamlessly accepts PostgreSQL, Redis, SQL Server, etc.");

        try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { /* Ignore */ }
    }

    /// <summary>
    /// Business service 100% decoupled from database infrastructure or cache broker.
    /// </summary>
    private static async Task ExecuteBusinessServiceAgnosticAsync(IDistributedLockProvider provider, string resourceId)
    {
        ConsoleUi.PrintInfo($"[Agnostic Service] Executing critical operation on '{resourceId}'...");
        var result = await provider.ExecuteWithLockAsync(resourceId, async (ct) =>
        {
            await Task.Delay(50, ct);
            ConsoleUi.PrintSuccess("[Agnostic Service] Operation executed safely within lock.");
        });

        if (result.IsSuccess)
        {
            ConsoleUi.PrintSuccess("[Agnostic Service] Lock cleanly released.");
        }
    }
}
