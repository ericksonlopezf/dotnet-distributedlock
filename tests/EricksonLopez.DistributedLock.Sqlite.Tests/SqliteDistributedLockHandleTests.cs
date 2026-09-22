// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EricksonLopez.DistributedLock.Sqlite.Tests;

public sealed class SqliteDistributedLockHandleTests
{
    [Fact]
    public void Constructor_NullConnection_ThrowsArgumentNullException()
    {
        var act = () => new SqliteDistributedLockHandle(
            null!,
            "res-1",
            "owner-1",
            NullLogger.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("connection");
    }

    [Fact]
    public void Constructor_NullResourceId_ThrowsArgumentNullException()
    {
        var conn = new FakeDbConnection();
        var act = () => new SqliteDistributedLockHandle(
            conn,
            null!,
            "owner-1",
            NullLogger.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("resourceId");
    }

    [Fact]
    public void Constructor_NullOwnerId_ThrowsArgumentNullException()
    {
        var conn = new FakeDbConnection();
        var act = () => new SqliteDistributedLockHandle(
            conn,
            "res-1",
            null!,
            NullLogger.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("ownerId");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var conn = new FakeDbConnection();
        var act = () => new SqliteDistributedLockHandle(
            conn,
            "res-1",
            "owner-1",
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task Properties_ReturnExpectedValues()
    {
        var conn = new FakeDbConnection();
        var expectedHash = BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes("sqlite-invoice-99")), 0);

        await using var handle = new SqliteDistributedLockHandle(
            conn,
            "sqlite-invoice-99",
            "owner-xyz",
            NullLogger.Instance,
            commandTimeoutSeconds: 45);

        handle.ResourceId.Should().Be("sqlite-invoice-99");
        handle.LockId.Should().Be(expectedHash);
        handle.HandleLostToken.IsCancellationRequested.Should().BeFalse();
        handle.FencingToken.Should().BeNull();
    }

    [Fact]
    public async Task Properties_WithFencingToken_ReturnsExpectedFencingToken()
    {
        var conn = new FakeDbConnection();
        await using var handle = new SqliteDistributedLockHandle(
            conn,
            "sqlite-fencing-test",
            "owner-fence",
            NullLogger.Instance,
            commandTimeoutSeconds: 30,
            fencingToken: 12345L);

        handle.FencingToken.Should().Be(12345L);
    }

    [Fact]
    public async Task DisposeAsync_WhenOpen_ExecutesDeleteCommandAndDisposesConnection()
    {
        var conn = new FakeDbConnection();
        FakeDbCommand? executedCmd = null;

        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            executedCmd = cmd;
            return Task.FromResult(1);
        };

        var handle = new SqliteDistributedLockHandle(
            conn,
            "order-delete-res",
            "owner-del-1",
            NullLogger.Instance,
            commandTimeoutSeconds: 25);

        await handle.DisposeAsync();

        executedCmd.Should().NotBeNull();
        executedCmd!.CommandText.Should().Be("DELETE FROM __distributed_locks WHERE resource_id = @ResourceId AND owner_id = @OwnerId;");
        executedCmd.CommandTimeout.Should().Be(25);
        executedCmd.Parameters["@ResourceId"].ParameterName.Should().Be("@ResourceId");
        executedCmd.Parameters["@ResourceId"].Value.Should().Be("order-delete-res");
        executedCmd.Parameters["@OwnerId"].ParameterName.Should().Be("@OwnerId");
        executedCmd.Parameters["@OwnerId"].Value.Should().Be("owner-del-1");

        conn.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_CalledMultipleTimes_IsIdempotent()
    {
        var conn = new FakeDbConnection();
        var executeCount = 0;

        conn.ExecuteNonQueryAsyncHandler = (_, _) =>
        {
            executeCount++;
            return Task.FromResult(1);
        };

        var handle = new SqliteDistributedLockHandle(
            conn,
            "res-multi-dispose",
            "owner-multi",
            NullLogger.Instance);

        await handle.DisposeAsync();
        await handle.DisposeAsync();
        await handle.DisposeAsync();

        executeCount.Should().Be(1);
        conn.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_WhenConnectionClosed_SkipsDeleteAndDisposesConnection()
    {
        var conn = new FakeDbConnection();
        conn.SetState(ConnectionState.Closed);
        FakeDbCommand? executedCmd = null;

        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            executedCmd = cmd;
            return Task.FromResult(1);
        };

        var handle = new SqliteDistributedLockHandle(
            conn,
            "res-closed",
            "owner-closed",
            NullLogger.Instance);

        await handle.DisposeAsync();

        executedCmd.Should().BeNull();
        conn.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_WhenExceptionThrownDuringDelete_LogsWarningAndStillDisposesConnection()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteNonQueryAsyncHandler = (_, _) => throw new InvalidOperationException("SQLite disk I/O error");

        var handle = new SqliteDistributedLockHandle(
            conn,
            "res-err",
            "owner-err",
            NullLogger.Instance);

        var act = () => handle.DisposeAsync().AsTask();
        await act.Should().NotThrowAsync();

        conn.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Keepalive_WhenRenewalFails_CancelsHandleLostToken()
    {
        var conn = new FakeDbConnection();
        var lostTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // When renewal runs, return 0 rows updated (lock was preempted/lost)
        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            if (cmd.CommandText.Contains("UPDATE __distributed_locks"))
            {
                return Task.FromResult(0);
            }
            return Task.FromResult(1);
        };

        var handle = new SqliteDistributedLockHandle(
            conn,
            "sqlite-lost-res",
            "owner-lost",
            NullLogger.Instance,
            lockTtl: TimeSpan.FromSeconds(60),
            keepaliveCadence: TimeSpan.FromMilliseconds(20));

        handle.HandleLostToken.Register(() => lostTcs.TrySetResult(true));

        var cancellationTriggered = await Task.WhenAny(lostTcs.Task, Task.Delay(2000));
        cancellationTriggered.Should().Be(lostTcs.Task);

        handle.HandleLostToken.IsCancellationRequested.Should().BeTrue();
        await handle.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_DisposesHandleLostCancellationTokenSource()
    {
        var conn = new FakeDbConnection();
        var handle = new SqliteDistributedLockHandle(
            conn,
            "res-disposal",
            "owner-disposal",
            NullLogger.Instance);

        var token = handle.HandleLostToken;
        await handle.DisposeAsync();

        var act = () => token.WaitHandle;
        act.Should().Throw<ObjectDisposedException>();
    }
}
