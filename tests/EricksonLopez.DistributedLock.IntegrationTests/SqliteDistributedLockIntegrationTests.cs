// Copyright © Erickson Lopez. MIT License.
using System;
using System.IO;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Sqlite;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EricksonLopez.DistributedLock.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class SqliteDistributedLockIntegrationTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public SqliteDistributedLockIntegrationTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"distlock_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath}";
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch
        {
            // Ignore temp file cleanup exceptions
        }
    }

    [Fact]
    public async Task Sqlite_SingleNode_AcquiresAndReleasesDeterministically()
    {
        var provider = new SqliteDistributedLockProvider(
            () => new SqliteConnection(_connectionString),
            NullLogger<SqliteDistributedLockProvider>.Instance);

        const string resource = "job:sqlite:batch-1";

        var lockResult = await provider.TryAcquireAsync(resource);
        lockResult.IsSuccess.Should().BeTrue();

        await using (lockResult.Value)
        {
            var secondaryAttempt = await provider.TryAcquireAsync(resource);
            secondaryAttempt.IsFailure.Should().BeTrue();
            secondaryAttempt.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
        }

        var reacquireResult = await provider.TryAcquireAsync(resource);
        reacquireResult.IsSuccess.Should().BeTrue();
        await reacquireResult.Value.DisposeAsync();
    }

    [Fact]
    public async Task Sqlite_ConcurrentContention_EnforcesMutualExclusion()
    {
        var provider1 = new SqliteDistributedLockProvider(
            () => new SqliteConnection(_connectionString),
            NullLogger<SqliteDistributedLockProvider>.Instance);

        var provider2 = new SqliteDistributedLockProvider(
            () => new SqliteConnection(_connectionString),
            NullLogger<SqliteDistributedLockProvider>.Instance);

        const string resource = "critical:sqlite:reconciliation";

        var lock1 = await provider1.TryAcquireAsync(resource);
        lock1.IsSuccess.Should().BeTrue();

        var lock2 = await provider2.TryAcquireAsync(resource);
        lock2.IsFailure.Should().BeTrue();
        lock2.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);

        await lock1.Value.DisposeAsync();

        var lock2Retry = await provider2.TryAcquireAsync(resource);
        lock2Retry.IsSuccess.Should().BeTrue();
        await lock2Retry.Value.DisposeAsync();
    }

    [Fact]
    public async Task Sqlite_TransactionScopedLock_ReleasesAutomaticallyOnRollback()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string resource = "tx:sqlite:transfer-funds";

        using (var transaction1 = connection.BeginTransaction())
        {
            var lock1 = await transaction1.TryAcquireInTransactionAsync(resource, NullLogger.Instance);
            lock1.IsSuccess.Should().BeTrue();

            transaction1.Rollback();
        }

        // After rollback, the lock row is automatically removed by the database rollback
        using (var transaction2 = connection.BeginTransaction())
        {
            var lock2 = await transaction2.TryAcquireInTransactionAsync(resource, NullLogger.Instance);
            lock2.IsSuccess.Should().BeTrue();

            transaction2.Commit();
        }
    }

    [Fact]
    public async Task Sqlite_CrashSimulation_RecoversOrphanedExpiredLock()
    {
        // Setup provider with short TTL of 1 second
        var provider = new SqliteDistributedLockProvider(
            () => new SqliteConnection(_connectionString),
            NullLogger<SqliteDistributedLockProvider>.Instance,
            new SqliteLockOptions { LockTtl = TimeSpan.FromSeconds(1) });

        const string resource = "crash:sqlite:orphaned-worker";

        // Step 1: Worker 1 acquires lock
        var lock1 = await provider.TryAcquireAsync(resource);
        lock1.IsSuccess.Should().BeTrue();

        // Step 2: Worker 1 "crashes" abruptly (SIGKILL/OOM) -> DisposeAsync() is NEVER called!

        // Step 3: Worker 2 immediately tries to acquire and fails (lock is still actively leased)
        var lock2Immediate = await provider.TryAcquireAsync(resource);
        lock2Immediate.IsFailure.Should().BeTrue();
        lock2Immediate.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);

        // Step 4: Wait for lease TTL to expire (1.2 seconds)
        await Task.Delay(1200);

        // Step 5: Worker 2 attempts acquisition -> detects expired orphaned lock and reclaims it
        var lock2Recovered = await provider.TryAcquireAsync(resource);
        lock2Recovered.IsSuccess.Should().BeTrue();
        await lock2Recovered.Value.DisposeAsync();
    }
}
