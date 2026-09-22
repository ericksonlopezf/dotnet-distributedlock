// Copyright © Erickson Lopez. MIT License.
using System;
using System.IO;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.MariaDb;
using EricksonLopez.DistributedLock.MySql;
using EricksonLopez.DistributedLock.Oracle;
using EricksonLopez.DistributedLock.PostgreSql;
using EricksonLopez.DistributedLock.Redis;
using EricksonLopez.DistributedLock.Showcase.Infrastructure;
using EricksonLopez.DistributedLock.Sqlite;
using EricksonLopez.DistributedLock.SqlServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EricksonLopez.DistributedLock.Showcase.Scenarios;

/// <summary>
/// Level 2 — Full Configuration: Advanced options across all providers, jitter control, TTL, keepalives, and OpenTelemetry.
/// </summary>
public static class Level02_FullConfiguration
{
    public static async Task RunAsync()
    {
        ConsoleUi.PrintHeader("Level 2 — Full Configuration",
            "Comprehensive tuning of all 7 providers: polling intervals, jitter, keepalives, TTL, OTel metrics.");

        var dbPath = Path.Combine(Path.GetTempPath(), "showcase_config.db");
        var connectionString = $"Data Source={dbPath};";

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

        // ── 1. SQLite — all available options ────────────────────────────────────────────
        ConsoleUi.PrintInfo("1. Configuring SqliteLockOptions (all properties)...");
        services.AddSqliteDistributedLock(connectionString, options =>
        {
            options.CommandTimeoutSeconds = 15;        // Maximum SQL command timeout
            options.RetryInterval = TimeSpan.FromMilliseconds(40); // Base retry interval under contention
            options.BackoffJitter = true;              // Randomized jitter to mitigate Thundering Herd
            options.LockTtl = TimeSpan.FromSeconds(45); // Lease TTL for catastrophic failure auto-reclaim
            options.KeepaliveCadence = TimeSpan.FromSeconds(15); // Background automatic lease renewal cadence
        });

        // ── 2. PostgreSQL — all available options ────────────────────────────────────────
        ConsoleUi.PrintInfo("2. Inspecting PostgresLockOptions (all properties)...");
        var pgOptions = new PostgresLockOptions
        {
            KeepaliveCadence = TimeSpan.FromSeconds(30),       // Session heartbeat
            InitialPollingInterval = TimeSpan.FromMilliseconds(20), // Initial polling attempt
            MaxPollingInterval = TimeSpan.FromMilliseconds(300),   // Exponential backoff ceiling
            JitterRatio = 0.30,                                     // 30% dynamic jitter
            CommandTimeoutSeconds = 20                              // Advisory lock command timeout
        };
        ConsoleUi.PrintMetric("PostgreSQL KeepaliveCadence", pgOptions.KeepaliveCadence);
        ConsoleUi.PrintMetric("PostgreSQL InitialPollingInterval", pgOptions.InitialPollingInterval);
        ConsoleUi.PrintMetric("PostgreSQL MaxPollingInterval", pgOptions.MaxPollingInterval);
        ConsoleUi.PrintMetric("PostgreSQL JitterRatio", pgOptions.JitterRatio);
        ConsoleUi.PrintMetric("PostgreSQL CommandTimeoutSeconds", pgOptions.CommandTimeoutSeconds, "s");

        // ── 3. SQL Server — all available options ────────────────────────────────────────
        ConsoleUi.PrintInfo("3. Inspecting SqlServerLockOptions (all properties)...");
        var sqlServerOptions = new SqlServerLockOptions
        {
            CommandTimeoutSeconds = 25,                             // sp_getapplock command timeout
            KeepaliveCadence = TimeSpan.FromSeconds(10),           // Session heartbeat (null = disabled)
            RetryInterval = TimeSpan.FromMilliseconds(60),         // Base polling interval
            BackoffJitter = true                                    // Jitter to reduce contention
        };
        ConsoleUi.PrintMetric("SQL Server CommandTimeoutSeconds", sqlServerOptions.CommandTimeoutSeconds, "s");
        ConsoleUi.PrintMetric("SQL Server KeepaliveCadence", sqlServerOptions.KeepaliveCadence);
        ConsoleUi.PrintMetric("SQL Server RetryInterval", sqlServerOptions.RetryInterval);
        ConsoleUi.PrintMetric("SQL Server BackoffJitter", sqlServerOptions.BackoffJitter);

        // ── 4. MySQL — all available options ─────────────────────────────────────────────
        ConsoleUi.PrintInfo("4. Inspecting MySqlLockOptions (all properties)...");
        var mySqlOptions = new MySqlLockOptions
        {
            CommandTimeoutSeconds = 30,                            // GET_LOCK / RELEASE_LOCK command timeout
            KeepaliveCadence = TimeSpan.FromSeconds(10),          // Session heartbeat (null = disabled)
            RetryInterval = TimeSpan.FromMilliseconds(50),        // Base polling interval under contention
            BackoffJitter = true                                   // Jitter to distribute retries
        };
        ConsoleUi.PrintMetric("MySQL CommandTimeoutSeconds", mySqlOptions.CommandTimeoutSeconds, "s");
        ConsoleUi.PrintMetric("MySQL KeepaliveCadence", mySqlOptions.KeepaliveCadence);
        ConsoleUi.PrintMetric("MySQL RetryInterval", mySqlOptions.RetryInterval);
        ConsoleUi.PrintMetric("MySQL BackoffJitter", mySqlOptions.BackoffJitter);

        // ── 5. MariaDB — all available options ───────────────────────────────────────────
        ConsoleUi.PrintInfo("5. Inspecting MariaDbLockOptions (all properties)...");
        var mariaDbOptions = new MariaDbLockOptions
        {
            CommandTimeoutSeconds = 30,                            // GET_LOCK / RELEASE_LOCK command timeout
            KeepaliveCadence = TimeSpan.FromSeconds(10),          // Session heartbeat (null = disabled)
            RetryInterval = TimeSpan.FromMilliseconds(50),        // Base polling interval under contention
            BackoffJitter = true                                   // Jitter to distribute retries
        };
        ConsoleUi.PrintMetric("MariaDB CommandTimeoutSeconds", mariaDbOptions.CommandTimeoutSeconds, "s");
        ConsoleUi.PrintMetric("MariaDB KeepaliveCadence", mariaDbOptions.KeepaliveCadence);
        ConsoleUi.PrintMetric("MariaDB RetryInterval", mariaDbOptions.RetryInterval);
        ConsoleUi.PrintMetric("MariaDB BackoffJitter", mariaDbOptions.BackoffJitter);

        // ── 6. Oracle — all available options ────────────────────────────────────────────
        ConsoleUi.PrintInfo("6. Inspecting OracleLockOptions (all properties)...");
        var oracleOptions = new OracleLockOptions
        {
            CommandTimeoutSeconds = 30,                            // DBMS_LOCK.REQUEST / RELEASE command timeout
            KeepaliveCadence = TimeSpan.FromSeconds(10),          // Session heartbeat (null = disabled)
            RetryInterval = TimeSpan.FromMilliseconds(50),        // Base polling interval
            BackoffJitter = true                                   // Jitter to distribute retries
        };
        ConsoleUi.PrintMetric("Oracle CommandTimeoutSeconds", oracleOptions.CommandTimeoutSeconds, "s");
        ConsoleUi.PrintMetric("Oracle KeepaliveCadence", oracleOptions.KeepaliveCadence);
        ConsoleUi.PrintMetric("Oracle RetryInterval", oracleOptions.RetryInterval);
        ConsoleUi.PrintMetric("Oracle BackoffJitter", oracleOptions.BackoffJitter);

        // ── 7. Redis — all available options ─────────────────────────────────────────────
        ConsoleUi.PrintInfo("7. Inspecting RedisLockOptions (all properties)...");
        var redisOptions = new RedisLockOptions
        {
            DefaultExpiry = TimeSpan.FromSeconds(30),             // Redis lease duration (SET NX PX)
            KeyPrefix = "corp:app:locks:",                         // Redis key prefix
            KeepaliveCadence = TimeSpan.FromSeconds(10),          // Lease renewal cadence before expiration
            RetryInterval = TimeSpan.FromMilliseconds(50),        // Polling under contention
            BackoffJitter = true                                   // Jitter to distribute retries
        };
        ConsoleUi.PrintMetric("Redis DefaultExpiry", redisOptions.DefaultExpiry);
        ConsoleUi.PrintMetric("Redis KeyPrefix", redisOptions.KeyPrefix);
        ConsoleUi.PrintMetric("Redis KeepaliveCadence", redisOptions.KeepaliveCadence);
        ConsoleUi.PrintMetric("Redis RetryInterval", redisOptions.RetryInterval);
        ConsoleUi.PrintMetric("Redis BackoffJitter", redisOptions.BackoffJitter);

        // ── 8. OpenTelemetry — all DistributedLockMetrics methods ───────────────────────
        ConsoleUi.PrintInfo("8. Recording full telemetry with DistributedLockMetrics...");
        DistributedLockMetrics.RecordAcquisition("config:demo:resource", "session", "acquired");
        DistributedLockMetrics.RecordWaitDuration("config:demo:resource", "session", "acquired", 12.45);
        DistributedLockMetrics.RecordHoldDuration("config:demo:resource", "session", 450.80);
        DistributedLockMetrics.RecordLockLost("config:demo:resource", 987654321L);
        ConsoleUi.PrintSuccess($"Metrics emitted to Meter '{DistributedLockMetrics.MeterName}' v{DistributedLockMetrics.MeterVersion}.");

        await using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IDistributedLockProvider>();

        // ── 9. TryAcquireAsync with timeout ────────────────────────────────────────────────────────
        ConsoleUi.PrintInfo("9. Executing TryAcquireAsync(resourceId, timeout) with applied options...");
        var handle = await provider.TryAcquireAsync("config:demo:resource", TimeSpan.FromSeconds(2));
        if (handle.IsSuccess)
        {
            await using (handle.Value)
            {
                ConsoleUi.PrintSuccess("Lock executed with optimized configuration and enabled jitter.");
            }
        }

        // ── 10. ExecuteWithLockAsync(string, TimeSpan, action) — third overload ────────────────
        ConsoleUi.PrintInfo("10. Demonstrating 'ExecuteWithLockAsync(resourceId, timeout, action)' — third overload of DistributedLockExtensions...");
        var scopeResult = await provider.ExecuteWithLockAsync(
            "config:scope:demo",
            TimeSpan.FromSeconds(3),
            async (ct) =>
            {
                ConsoleUi.PrintInfo("  → Inside scope guard with bounded timeout.");
                await Task.Delay(20, ct);
            });

        if (scopeResult.IsSuccess)
        {
            ConsoleUi.PrintSuccess("ExecuteWithLockAsync(resourceId, timeout, action) completed successfully.");
        }

        try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { /* Ignore */ }
    }
}

