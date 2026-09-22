// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.MySql;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace EricksonLopez.DistributedLock.MySql.Tests;

public sealed class MySqlDistributedLockHandleTests
{
    [Fact]
    public void Constructor_NullConnection_ThrowsArgumentNullException()
    {
        var act = () => new MySqlDistributedLockHandle(
            null!,
            "res-key",
            "res-id",
            NullLogger.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("connection");
    }

    [Fact]
    public void Constructor_NullLockResource_ThrowsArgumentNullException()
    {
        var conn = new FakeDbConnection();
        var act = () => new MySqlDistributedLockHandle(
            conn,
            null!,
            "res-id",
            NullLogger.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("lockResource");
    }

    [Fact]
    public void Constructor_NullResourceId_ThrowsArgumentNullException()
    {
        var conn = new FakeDbConnection();
        var act = () => new MySqlDistributedLockHandle(
            conn,
            "res-key",
            null!,
            NullLogger.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("resourceId");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var conn = new FakeDbConnection();
        var act = () => new MySqlDistributedLockHandle(
            conn,
            "res-key",
            "res-id",
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task Properties_ReturnExpectedValues()
    {
        var conn = new FakeDbConnection();
        var expectedHash = BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes("mysql-order-123")), 0);

        await using var handle = new MySqlDistributedLockHandle(
            conn,
            "mysql-order-123",
            "mysql-order-123",
            NullLogger.Instance,
            keepaliveCadence: null,
            commandTimeoutSeconds: 45);

        handle.ResourceId.Should().Be("mysql-order-123");
        handle.LockId.Should().Be(expectedHash);
        handle.HandleLostToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task Constructor_ZeroKeepaliveCadence_DoesNotStartTimer()
    {
        var conn = new FakeDbConnection();
        var queryExecuted = false;

        conn.ExecuteScalarHandler = _ =>
        {
            queryExecuted = true;
            return 1;
        };

        await using var handle = new MySqlDistributedLockHandle(
            conn,
            "res-key",
            "res-id",
            NullLogger.Instance,
            keepaliveCadence: TimeSpan.Zero);

        await Task.Delay(50);
        queryExecuted.Should().BeFalse();
    }

    [Fact]
    public async Task Keepalive_WhenSuccessful_MaintainsActiveHandleToken()
    {
        var conn = new FakeDbConnection();
        var pingTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        conn.ExecuteScalarHandler = cmd =>
        {
            if (cmd.CommandText == "SELECT 1;")
            {
                pingTcs.TrySetResult(true);
            }
            return 1;
        };

        var handle = new MySqlDistributedLockHandle(
            conn,
            "res-key",
            "res-id",
            NullLogger.Instance,
            keepaliveCadence: TimeSpan.FromMilliseconds(20),
            commandTimeoutSeconds: 15);

        var pingReceived = await Task.WhenAny(pingTcs.Task, Task.Delay(2000));
        pingReceived.Should().Be(pingTcs.Task);

        handle.HandleLostToken.IsCancellationRequested.Should().BeFalse();
        await handle.DisposeAsync();
    }

    [Fact]
    public async Task Keepalive_WhenQueryFails_CancelsHandleLostToken()
    {
        var conn = new FakeDbConnection();
        var lostTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        conn.ExecuteScalarHandler = cmd =>
        {
            if (cmd.CommandText == "SELECT 1;")
            {
                throw new TimeoutException("Database connection terminated.");
            }
            return 1;
        };

        var handle = new MySqlDistributedLockHandle(
            conn,
            "res-key",
            "res-id",
            NullLogger.Instance,
            keepaliveCadence: TimeSpan.FromMilliseconds(20));

        handle.HandleLostToken.Register(() => lostTcs.TrySetResult(true));

        var cancellationTriggered = await Task.WhenAny(lostTcs.Task, Task.Delay(2000));
        cancellationTriggered.Should().Be(lostTcs.Task);

        handle.HandleLostToken.IsCancellationRequested.Should().BeTrue();
        await handle.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_ReleasesLockAndDisposesConnection()
    {
        var conn = new FakeDbConnection();
        FakeDbCommand? releaseCommand = null;

        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            releaseCommand = cmd;
            return Task.FromResult(1);
        };

        var handle = new MySqlDistributedLockHandle(
            conn,
            "my-lock-resource",
            "my-resource",
            NullLogger.Instance,
            keepaliveCadence: null,
            commandTimeoutSeconds: 25);

        await handle.DisposeAsync();

        releaseCommand.Should().NotBeNull();
        releaseCommand!.CommandText.Should().Contain("RELEASE_LOCK");
        releaseCommand.CommandTimeout.Should().Be(25);
        releaseCommand.Parameters["@Resource"].Value.Should().Be("my-lock-resource");

        conn.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_WhenConnectionNotOpen_SkipsReleaseCommand()
    {
        var conn = new FakeDbConnection();
        conn.SetState(ConnectionState.Closed);

        var handle = new MySqlDistributedLockHandle(
            conn,
            "res-key",
            "res-id",
            NullLogger.Instance,
            keepaliveCadence: null);

        await handle.DisposeAsync();

        conn.CommandsCreated.Should().BeEmpty();
        conn.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent()
    {
        var conn = new FakeDbConnection();
        var executionCount = 0;

        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            executionCount++;
            return Task.FromResult(1);
        };

        var handle = new MySqlDistributedLockHandle(
            conn,
            "res-key",
            "res-id",
            NullLogger.Instance,
            keepaliveCadence: null);

        await handle.DisposeAsync();
        await handle.DisposeAsync();

        executionCount.Should().Be(1);
        conn.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_WhenReleaseCommandThrows_SwallowsExceptionAndDisposesConnection()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteNonQueryAsyncHandler = (_, _) => throw new InvalidOperationException("Connection severed");

        var handle = new MySqlDistributedLockHandle(
            conn,
            "res-key",
            "res-id",
            NullLogger.Instance,
            keepaliveCadence: null);

        var act = () => handle.DisposeAsync().AsTask();
        await act.Should().NotThrowAsync();

        conn.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ExecuteKeepalive_WhenDisposed_ReturnsEarlyWithoutQuery()
    {
        var conn = new FakeDbConnection();
        var queryExecuted = false;

        conn.ExecuteScalarHandler = cmd =>
        {
            queryExecuted = true;
            return 1;
        };

        var handle = new MySqlDistributedLockHandle(
            conn,
            "res-key",
            "res-id",
            NullLogger.Instance);

        await handle.DisposeAsync();

        var method = typeof(MySqlDistributedLockHandle).GetMethod("ExecuteKeepalive", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method.Should().NotBeNull();
        method!.Invoke(handle, new object?[] { null });

        queryExecuted.Should().BeFalse();
    }

    [Fact]
    public void ExecuteKeepalive_WhenCtsDisposed_HandlesObjectDisposedExceptionGracefully()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteScalarHandler = _ => throw new InvalidOperationException("Ping failure");

        var handle = new MySqlDistributedLockHandle(
            conn,
            "res-key",
            "res-id",
            NullLogger.Instance);

        var ctsField = typeof(MySqlDistributedLockHandle).GetField("_handleLostCts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        ctsField.Should().NotBeNull();
        var cts = (CancellationTokenSource)ctsField!.GetValue(handle)!;
        cts.Dispose();

        var method = typeof(MySqlDistributedLockHandle).GetMethod("ExecuteKeepalive", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var act = () => method!.Invoke(handle, new object?[] { null });
        act.Should().NotThrow();
    }

    [Fact]
    public async Task DisposeAsync_DisposesHandleLostCancellationTokenSource()
    {
        var conn = new FakeDbConnection();
        var handle = new MySqlDistributedLockHandle(
            conn,
            "res-key",
            "res-id",
            NullLogger.Instance);

        var token = handle.HandleLostToken;
        await handle.DisposeAsync();

        var act = () => token.WaitHandle;
        act.Should().Throw<ObjectDisposedException>();
    }
}
