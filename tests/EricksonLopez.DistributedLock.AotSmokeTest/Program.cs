// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.MariaDb;
using EricksonLopez.DistributedLock.MySql;
using EricksonLopez.DistributedLock.Oracle;
using EricksonLopez.DistributedLock.PostgreSql;
using EricksonLopez.DistributedLock.Redis;
using EricksonLopez.DistributedLock.Sqlite;
using EricksonLopez.DistributedLock.SqlServer;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

Console.WriteLine("==========================================================");
Console.WriteLine(" EricksonLopez.DistributedLock NativeAOT Test Suite       ");
Console.WriteLine("==========================================================");

int passedTests = 0;

void Assert([DoesNotReturnIf(false)] bool condition, string testName)
{
    if (!condition)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[FAIL] {testName}");
        Console.ResetColor();
        throw new InvalidOperationException($"Assertion failed for: {testName}");
    }

    passedTests++;
    Console.WriteLine($"[PASS] {testName}");
}

// ── 1. Error Domain & Attributes ───────────────────────────────────────────
Console.WriteLine("\n--- 1. DistributedLock Errors & Attributes ---");

Assert(DistributedLockErrors.LockAlreadyHeld.Code == "DistributedLock.AlreadyHeld", "LockAlreadyHeld code matches");
Assert(DistributedLockErrors.Timeout.Code == "DistributedLock.Timeout", "Timeout code matches");
Assert(DistributedLockErrors.LockLost.Code == "DistributedLock.Lost", "LockLost code matches");
Assert(DistributedLockErrors.Canceled.Code == "DistributedLock.Canceled", "Canceled code matches");

var attr = new DistributedLockAttribute("orders:{OrderId}")
{
    TimeoutSeconds = 15,
    Blocking = true
};
Assert(attr.ResourceKeyPattern == "orders:{OrderId}", "DistributedLockAttribute preserves resource key pattern");
Assert(attr.TimeoutSeconds == 15, "DistributedLockAttribute preserves timeout");
Assert(attr.Blocking, "DistributedLockAttribute preserves blocking flag");

// ── 2. Dialect Lock Options Invariants ─────────────────────────────────────
Console.WriteLine("\n--- 2. Dialect Lock Options Invariants ---");

var pgOpts = new PostgresLockOptions
{
    CommandTimeoutSeconds = 12,
    KeepaliveCadence = TimeSpan.FromSeconds(3),
    InitialPollingInterval = TimeSpan.FromMilliseconds(20)
};
Assert(pgOpts.CommandTimeoutSeconds == 12, "PostgresLockOptions configured correctly");
Assert(pgOpts.KeepaliveCadence == TimeSpan.FromSeconds(3), "PostgresLockOptions KeepaliveCadence is set");

var sqlOpts = new SqlServerLockOptions
{
    CommandTimeoutSeconds = 25,
    KeepaliveCadence = TimeSpan.FromSeconds(5)
};
Assert(sqlOpts.CommandTimeoutSeconds == 25, "SqlServerLockOptions CommandTimeoutSeconds is 25");

var mySqlOpts = new MySqlLockOptions { CommandTimeoutSeconds = 20, KeepaliveCadence = TimeSpan.FromSeconds(5) };
Assert(mySqlOpts.CommandTimeoutSeconds == 20, "MySqlLockOptions configured correctly");

var mariaOpts = new MariaDbLockOptions { CommandTimeoutSeconds = 20, KeepaliveCadence = TimeSpan.FromSeconds(5) };
Assert(mariaOpts.CommandTimeoutSeconds == 20, "MariaDbLockOptions configured correctly");

var oraOpts = new OracleLockOptions { CommandTimeoutSeconds = 30, KeepaliveCadence = TimeSpan.FromSeconds(6) };
Assert(oraOpts.CommandTimeoutSeconds == 30, "OracleLockOptions configured correctly");

var sqliteOpts = new SqliteLockOptions
{
    CommandTimeoutSeconds = 10,
    RetryInterval = TimeSpan.FromMilliseconds(75),
    BackoffJitter = true
};
Assert(sqliteOpts.CommandTimeoutSeconds == 10, "SqliteLockOptions CommandTimeoutSeconds is 10");
Assert(sqliteOpts.RetryInterval == TimeSpan.FromMilliseconds(75), "SqliteLockOptions RetryInterval is 75ms");
Assert(sqliteOpts.BackoffJitter, "SqliteLockOptions BackoffJitter is true");

var redisOpts = new RedisLockOptions
{
    KeyPrefix = "aot_lock:",
    DefaultExpiry = TimeSpan.FromSeconds(30),
    KeepaliveCadence = TimeSpan.FromSeconds(10)
};
Assert(redisOpts.KeyPrefix == "aot_lock:", "RedisLockOptions prefix is configured");
Assert(redisOpts.DefaultExpiry == TimeSpan.FromSeconds(30), "RedisLockOptions DefaultExpiry is configured");

// ── 3. Physical SQLite Distributed Lock Execution ─────────────────────────
Console.WriteLine("\n--- 3. Physical SqliteDistributedLock Execution ---");

using var masterConn = new SqliteConnection("Data Source=aot_lock_db;Mode=Memory;Cache=Shared");
masterConn.Open();

Func<DbConnection> connectionFactory = () =>
{
    var conn = new SqliteConnection("Data Source=aot_lock_db;Mode=Memory;Cache=Shared");
    conn.Open();
    return conn;
};

var provider = new SqliteDistributedLockProvider(
    connectionFactory,
    NullLogger<SqliteDistributedLockProvider>.Instance,
    new SqliteLockOptions());

// 3a. Successful lock acquisition
Console.WriteLine("Acquiring SQLite distributed lock...");
var acquireResult = await provider.AcquireHandleAsync("test_aot_resource");
Assert(acquireResult.IsSuccess, "AcquireHandleAsync returned success");

var handle = acquireResult.Value;
Assert(handle is not null, "Handle is not null");
Assert(handle.ResourceId == "test_aot_resource", "Handle resource name matches");

// 3b. Concurrent lock acquisition should detect conflict / timeout
Console.WriteLine("Testing concurrent conflict detection...");
var concurrentResult = await provider.TryAcquireHandleAsync("test_aot_resource", TimeSpan.FromMilliseconds(100));
Assert(!concurrentResult.IsSuccess, "Concurrent lock attempt fails as expected due to mutual exclusion");
Assert(concurrentResult.Error.Code == DistributedLockErrors.LockAlreadyHeld.Code || concurrentResult.Error.Code == DistributedLockErrors.Timeout.Code,
    "Conflict error code matches DistributedLockErrors.LockAlreadyHeld or Timeout");

// 3c. Release lock via DisposeAsync
Console.WriteLine("Releasing SQLite distributed lock...");
await handle.DisposeAsync();
Assert(true, "Handle disposed without error");

// 3d. Re-acquire lock to confirm clean release
Console.WriteLine("Re-acquiring lock after release...");
var reacquireResult = await provider.AcquireHandleAsync("test_aot_resource");
Assert(reacquireResult.IsSuccess, "Re-acquisition succeeds following clean release");

await reacquireResult.Value.DisposeAsync();
Assert(true, "Reacquired handle successfully released");

Console.WriteLine("\n==========================================================");
Console.WriteLine($" All {passedTests} NativeAOT Tests PASSED Successfully!   ");
Console.WriteLine("==========================================================");
return 0;
