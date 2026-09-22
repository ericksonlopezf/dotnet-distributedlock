// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Sqlite;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EricksonLopez.DistributedLock.IntegrationTests;

[Trait("Category", "Adversarial")]
public sealed class AdversarialTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public AdversarialTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"adversarial_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath};Cache=Shared;Mode=ReadWriteCreate;";

        // Initialize WAL mode for maximal concurrency
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;";
        cmd.ExecuteNonQuery();
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

    private SqliteDistributedLockProvider CreateProvider(SqliteLockOptions? options = null)
    {
        return new SqliteDistributedLockProvider(
            () => new SqliteConnection(_connectionString),
            NullLogger<SqliteDistributedLockProvider>.Instance,
            options ?? new SqliteLockOptions { CommandTimeoutSeconds = 15 });
    }

    [Fact]
    public async Task Adversarial_Acquire_Then_Acquire_SameResource_ReturnsConflict()
    {
        var provider1 = CreateProvider();
        var provider2 = CreateProvider();
        const string resource = "adv:single-lock";

        var lock1 = await provider1.TryAcquireAsync(resource);
        lock1.IsSuccess.Should().BeTrue("First acquisition must succeed");

        var lock2 = await provider2.TryAcquireAsync(resource);
        lock2.IsFailure.Should().BeTrue("Second immediate acquisition must fail");
        lock2.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);

        await lock1.Value.DisposeAsync();

        var lock2Retry = await provider2.TryAcquireAsync(resource);
        lock2Retry.IsSuccess.Should().BeTrue("Reacquisition after release must succeed");
        await lock2Retry.Value.DisposeAsync();
    }

    [Fact]
    public async Task Adversarial_Double_Release_Is_Idempotent_And_Safe()
    {
        var provider = CreateProvider();
        const string resource = "adv:double-release";

        var lockResult = await provider.TryAcquireAsync(resource);
        lockResult.IsSuccess.Should().BeTrue();

        var handle = lockResult.Value;

        // Concurrent multi-release
        var releaseTasks = Enumerable.Range(0, 10).Select(_ => handle.DisposeAsync().AsTask()).ToArray();
        Func<Task> act = async () => await Task.WhenAll(releaseTasks);

        await act.Should().NotThrowAsync("Multiple concurrent releases must be idempotent");

        // Verify resource can be reacquired immediately
        var reacquire = await provider.TryAcquireAsync(resource);
        reacquire.IsSuccess.Should().BeTrue();
        await reacquire.Value.DisposeAsync();
    }

    [Fact]
    public async Task Adversarial_Concurrent_Release_And_Acquire_Preserves_Mutual_Exclusion()
    {
        var provider = CreateProvider();
        const string resource = "adv:release-acquire-race";

        for (int iteration = 0; iteration < 20; iteration++)
        {
            var lock1 = await provider.TryAcquireAsync(resource);
            lock1.IsSuccess.Should().BeTrue();

            var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var releaseTask = Task.Run(async () =>
            {
                await startGate.Task;
                await Task.Delay(1);
                await lock1.Value.DisposeAsync();
            });

            var acquireTask = Task.Run(async () =>
            {
                await startGate.Task;
                return await provider.TryAcquireAsync(resource, TimeSpan.FromMilliseconds(500));
            });

            startGate.SetResult();
            await releaseTask;
            var acquireResult = await acquireTask;

            acquireResult.IsSuccess.Should().BeTrue("Contender must acquire once released without race corruption");
            await acquireResult.Value.DisposeAsync();
        }
    }

    [Fact]
    public async Task Adversarial_Cancel_During_Acquiring_Does_Not_Leave_Orphaned_Lock()
    {
        var provider = CreateProvider();
        const string resource = "adv:cancel-acquiring";

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancelled immediately

        var result = await provider.TryAcquireAsync(resource, cancellationToken: cts.Token);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);

        // Lock must remain free
        var subsequentAcquisition = await provider.TryAcquireAsync(resource);
        subsequentAcquisition.IsSuccess.Should().BeTrue("No orphaned lock should exist after canceled acquisition");
        await subsequentAcquisition.Value.DisposeAsync();
    }

    [Fact]
    public async Task Adversarial_Cancel_During_Polling_Aborts_Promptly()
    {
        var provider = CreateProvider();
        const string resource = "adv:cancel-polling";

        var heldLock = await provider.TryAcquireAsync(resource);
        heldLock.IsSuccess.Should().BeTrue();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var timeoutResult = await provider.TryAcquireAsync(resource, TimeSpan.FromSeconds(10), cts.Token);
        sw.Stop();

        timeoutResult.IsFailure.Should().BeTrue();
        timeoutResult.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
        sw.ElapsedMilliseconds.Should().BeLessThan(2000, "Must abort well before the 10s timeout when canceled");

        await heldLock.Value.DisposeAsync();
    }

    [Fact]
    public async Task Adversarial_High_Contention_Thrashing_50_Tasks_No_Double_Ownership()
    {
        var provider = CreateProvider(new SqliteLockOptions
        {
            CommandTimeoutSeconds = 15,
            RetryInterval = TimeSpan.FromMilliseconds(5),
            BackoffJitter = true
        });

        const string resource = "adv:thrashing-resource";
        int activeOwners = 0;
        int maxSimultaneousOwners = 0;
        int successfulCriticalSections = 0;

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int contenders = 50;
        int ready = 0;

        var tasks = Enumerable.Range(0, contenders).Select(async _ =>
        {
            Interlocked.Increment(ref ready);
            await startGate.Task;

            // Attempt acquisition with up to 2 seconds wait
            var lockResult = await provider.TryAcquireAsync(resource, TimeSpan.FromSeconds(2));
            if (lockResult.IsSuccess)
            {
                int current = Interlocked.Increment(ref activeOwners);
                // Update peak owners observed
                int prevMax;
                do
                {
                    prevMax = Volatile.Read(ref maxSimultaneousOwners);
                    if (current <= prevMax) break;
                } while (Interlocked.CompareExchange(ref maxSimultaneousOwners, current, prevMax) != prevMax);

                // Simulate critical section
                await Task.Delay(5);

                Interlocked.Decrement(ref activeOwners);
                Interlocked.Increment(ref successfulCriticalSections);

                await lockResult.Value.DisposeAsync();
            }
        }).ToArray();

        while (Volatile.Read(ref ready) < contenders)
        {
            await Task.Yield();
        }
        startGate.SetResult();

        await Task.WhenAll(tasks);

        maxSimultaneousOwners.Should().Be(1, "MUTUAL EXCLUSION VIOLATION: More than 1 owner executed critical section simultaneously!");
        successfulCriticalSections.Should().BeGreaterThan(0, "At least one contender should have succeeded");
    }

    [Fact]
    public async Task Adversarial_ExecuteWithLockAsync_Releases_On_Exception()
    {
        var provider = CreateProvider();
        const string resource = "adv:scope-exception";

        Func<Task> act = async () =>
        {
            await provider.ExecuteWithLockAsync(resource, _ => throw new InvalidOperationException("Simulated domain crash"));
        };

        await act.Should().ThrowAsync<InvalidOperationException>();

        // Lock must have been cleanly released despite unhandled exception in action
        var reacquire = await provider.TryAcquireAsync(resource);
        reacquire.IsSuccess.Should().BeTrue("Lock handle must be released when action throws");
        await reacquire.Value.DisposeAsync();
    }

    [Fact]
    public async Task Adversarial_Connection_Severance_Detects_Loss_And_Cancels_HandleLostToken()
    {
        // Provider with short 100ms keepalive
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        var provider = new SqliteDistributedLockProvider(
            () => connection,
            NullLogger<SqliteDistributedLockProvider>.Instance,
            new SqliteLockOptions
            {
                KeepaliveCadence = TimeSpan.FromMilliseconds(100)
            });

        const string resource = "adv:socket-severance";

        var handleResult = await provider.TryAcquireHandleAsync(resource);
        handleResult.IsSuccess.Should().BeTrue();
        var handle = handleResult.Value;

        // Simulate sudden TCP socket drop / connection close
        await connection.CloseAsync();

        // Wait for keepalive loop to detect closed connection
        using var waitCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!handle.HandleLostToken.IsCancellationRequested && !waitCts.IsCancellationRequested)
        {
            await Task.Delay(50);
        }

        handle.HandleLostToken.IsCancellationRequested.Should().BeTrue(
            "HandleLostToken MUST be canceled when underlying connection drops!");

        await handle.DisposeAsync();
    }
}
