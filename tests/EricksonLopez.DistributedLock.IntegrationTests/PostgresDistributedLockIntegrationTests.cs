// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.PostgreSql;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace EricksonLopez.DistributedLock.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class PostgresDistributedLockIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("distlock_db")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    private string ConnectionString => _container.GetConnectionString();

    [Fact]
    public async Task TryAcquireAsync_SingleNode_AcquiresAndReleasesDeterministically()
    {
        var provider = new PostgresDistributedLockProvider(
            () => new NpgsqlConnection(ConnectionString),
            NullLogger<PostgresDistributedLockProvider>.Instance);

        const string resource = "job:invoices:batch-1";

        var lockResult = await provider.TryAcquireAsync(resource);
        lockResult.IsSuccess.Should().BeTrue();

        await using (lockResult.Value)
        {
            // Lock is held
            var secondaryAttempt = await provider.TryAcquireAsync(resource);
            secondaryAttempt.IsFailure.Should().BeTrue();
            secondaryAttempt.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
        }

        // After disposal, lock is available again
        var reacquireResult = await provider.TryAcquireAsync(resource);
        reacquireResult.IsSuccess.Should().BeTrue();
        await reacquireResult.Value.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_ConcurrentContention_EnforcesMutualExclusion()
    {
        var provider1 = new PostgresDistributedLockProvider(
            () => new NpgsqlConnection(ConnectionString),
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var provider2 = new PostgresDistributedLockProvider(
            () => new NpgsqlConnection(ConnectionString),
            NullLogger<PostgresDistributedLockProvider>.Instance);

        const string resource = "critical:reconciliation";

        var lock1 = await provider1.TryAcquireAsync(resource);
        lock1.IsSuccess.Should().BeTrue();

        var lock2 = await provider2.TryAcquireAsync(resource);
        lock2.IsFailure.Should().BeTrue();
        lock2.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);

        await lock1.Value.DisposeAsync();

        // Provider 2 can now acquire
        var lock2Retry = await provider2.TryAcquireAsync(resource);
        lock2Retry.IsSuccess.Should().BeTrue();
        await lock2Retry.Value.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_WithTimeout_WaitsAndSucceedsOnceReleased()
    {
        var provider1 = new PostgresDistributedLockProvider(
            () => new NpgsqlConnection(ConnectionString),
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var provider2 = new PostgresDistributedLockProvider(
            () => new NpgsqlConnection(ConnectionString),
            NullLogger<PostgresDistributedLockProvider>.Instance,
            new PostgresLockOptions
            {
                InitialPollingInterval = TimeSpan.FromMilliseconds(20),
                MaxPollingInterval = TimeSpan.FromMilliseconds(50),
                JitterRatio = 0.2
            });

        const string resource = "job:materialized-view:refresh";

        var lock1 = await provider1.TryAcquireAsync(resource);
        lock1.IsSuccess.Should().BeTrue();

        // Launch delayed release in background
        _ = Task.Run(async () =>
        {
            await Task.Delay(200);
            await lock1.Value.DisposeAsync();
        });

        // Provider 2 polls with timeout
        var lock2 = await provider2.TryAcquireAsync(resource, TimeSpan.FromSeconds(5));
        lock2.IsSuccess.Should().BeTrue();

        await lock2.Value.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_Blocking_AcquiresWhenReleased()
    {
        var provider1 = new PostgresDistributedLockProvider(
            () => new NpgsqlConnection(ConnectionString),
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var provider2 = new PostgresDistributedLockProvider(
            () => new NpgsqlConnection(ConnectionString),
            NullLogger<PostgresDistributedLockProvider>.Instance);

        const string resource = "job:exclusive:billing-cycle";

        var lock1 = await provider1.TryAcquireAsync(resource);
        lock1.IsSuccess.Should().BeTrue();

        // Release after 300 ms
        _ = Task.Run(async () =>
        {
            await Task.Delay(300);
            await lock1.Value.DisposeAsync();
        });

        var lock2 = await provider2.AcquireAsync(resource);
        lock2.IsSuccess.Should().BeTrue();

        await lock2.Value.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireHandleAsync_ReturnsStronglyTypedHandle()
    {
        var provider = new PostgresDistributedLockProvider(
            () => new NpgsqlConnection(ConnectionString),
            NullLogger<PostgresDistributedLockProvider>.Instance,
            new PostgresLockOptions
            {
                KeepaliveCadence = TimeSpan.FromMilliseconds(150)
            });

        const string resource = "workers:health-check";

        var handleResult = await provider.TryAcquireHandleAsync(resource);
        handleResult.IsSuccess.Should().BeTrue();

        var handle = handleResult.Value;
        handle.ResourceId.Should().Be(resource);
        handle.LockId.Should().NotBe(0);
        handle.HandleLostToken.IsCancellationRequested.Should().BeFalse();

        // Wait for multiple keepalive ticks
        await Task.Delay(400);
        handle.HandleLostToken.IsCancellationRequested.Should().BeFalse();

        await handle.DisposeAsync();
    }

    [Fact]
    public async Task KeepaliveHeartbeat_WhenConnectionSevered_CancelsHandleLostToken()
    {
        NpgsqlConnection? activeConnection = null;

        var provider = new PostgresDistributedLockProvider(
            () =>
            {
                activeConnection = new NpgsqlConnection(ConnectionString);
                return activeConnection;
            },
            NullLogger<PostgresDistributedLockProvider>.Instance,
            new PostgresLockOptions
            {
                KeepaliveCadence = TimeSpan.FromMilliseconds(100)
            });

        const string resource = "network:sever-test";

        var handleResult = await provider.TryAcquireHandleAsync(resource);
        handleResult.IsSuccess.Should().BeTrue();

        var handle = handleResult.Value;
        handle.HandleLostToken.IsCancellationRequested.Should().BeFalse();

        // Sever the underlying connection
        await activeConnection!.CloseAsync();

        // Wait for keepalive heartbeat to detect dropped connection
        for (var i = 0; i < 30; i++)
        {
            if (handle.HandleLostToken.IsCancellationRequested)
            {
                break;
            }
            await Task.Delay(50);
        }

        handle.HandleLostToken.IsCancellationRequested.Should().BeTrue();

        await handle.DisposeAsync();
    }

    [Fact]
    public async Task TransactionScopedLock_ReleasesAutomaticallyOnRollback()
    {
        await using var connection1 = new NpgsqlConnection(ConnectionString);
        await connection1.OpenAsync();
        await using var transaction1 = await connection1.BeginTransactionAsync();

        await using var connection2 = new NpgsqlConnection(ConnectionString);
        await connection2.OpenAsync();
        await using var transaction2 = await connection2.BeginTransactionAsync();

        const string resource = "tx:account:transfer-funds";

        var lock1 = await transaction1.TryAcquireInTransactionAsync(resource, NullLogger.Instance);
        lock1.IsSuccess.Should().BeTrue();

        var lock2 = await transaction2.TryAcquireInTransactionAsync(resource, NullLogger.Instance);
        lock2.IsFailure.Should().BeTrue();
        lock2.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);

        // Roll back transaction 1
        await transaction1.RollbackAsync();

        // Now transaction 2 can acquire
        var lock2Retry = await transaction2.TryAcquireInTransactionAsync(resource, NullLogger.Instance);
        lock2Retry.IsSuccess.Should().BeTrue();

        await transaction2.CommitAsync();
    }
}
