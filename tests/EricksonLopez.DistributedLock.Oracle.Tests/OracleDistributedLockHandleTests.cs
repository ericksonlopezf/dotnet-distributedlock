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

namespace EricksonLopez.DistributedLock.Oracle.Tests;

public sealed class OracleDistributedLockHandleTests
{
    [Fact]
    public void Constructor_NullConnection_ThrowsArgumentNullException()
    {
        var act = () => new OracleDistributedLockHandle(
            null!,
            "ora-handle-1",
            "res-1",
            NullLogger.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("connection");
    }

    [Fact]
    public void Constructor_NullLockHandle_ThrowsArgumentNullException()
    {
        var conn = new FakeDbConnection();
        var act = () => new OracleDistributedLockHandle(
            conn,
            null!,
            "res-1",
            NullLogger.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("lockHandle");
    }

    [Fact]
    public void Constructor_NullResourceId_ThrowsArgumentNullException()
    {
        var conn = new FakeDbConnection();
        var act = () => new OracleDistributedLockHandle(
            conn,
            "ora-handle-1",
            null!,
            NullLogger.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("resourceId");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var conn = new FakeDbConnection();
        var act = () => new OracleDistributedLockHandle(
            conn,
            "ora-handle-1",
            "res-1",
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task Properties_ReturnExpectedValues()
    {
        var conn = new FakeDbConnection();
        var expectedHash = BitConverter.ToInt64(SHA256.HashData(Encoding.UTF8.GetBytes("ora-invoice-99")), 0);

        await using var handle = new OracleDistributedLockHandle(
            conn,
            "ora-handle-xyz",
            "ora-invoice-99",
            NullLogger.Instance,
            keepaliveCadence: null,
            commandTimeoutSeconds: 45);

        handle.ResourceId.Should().Be("ora-invoice-99");
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

        await using (new OracleDistributedLockHandle(
            conn,
            "ora-handle-1",
            "res-zero-cadence",
            NullLogger.Instance,
            keepaliveCadence: TimeSpan.Zero))
        {
            await Task.Delay(50);
            queryExecuted.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Keepalive_WhenSuccessful_KeepsHandleActive()
    {
        var conn = new FakeDbConnection();
        var queryCount = 0;
        string? commandText = null;

        conn.ExecuteScalarHandler = cmd =>
        {
            Interlocked.Increment(ref queryCount);
            commandText = cmd.CommandText;
            return 1;
        };

        await using var handle = new OracleDistributedLockHandle(
            conn,
            "ora-handle-1",
            "res-keepalive-ok",
            NullLogger.Instance,
            keepaliveCadence: TimeSpan.FromMilliseconds(20));

        await Task.Delay(80);

        queryCount.Should().BeGreaterThan(0);
        commandText.Should().Be("SELECT 1 FROM DUAL");
        handle.HandleLostToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task Keepalive_WhenDisposed_DoesNotExecuteQuery()
    {
        var conn = new FakeDbConnection();
        var queryCount = 0;

        conn.ExecuteScalarHandler = _ =>
        {
            Interlocked.Increment(ref queryCount);
            return 1;
        };

        var handle = new OracleDistributedLockHandle(
            conn,
            "ora-handle-1",
            "res-keepalive-disposed",
            NullLogger.Instance,
            keepaliveCadence: TimeSpan.FromMilliseconds(20));

        await handle.DisposeAsync();

        var method = typeof(OracleDistributedLockHandle).GetMethod("ExecuteKeepalive", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        method!.Invoke(handle, new object?[] { null });

        queryCount.Should().Be(0);
    }

    [Fact]
    public async Task Keepalive_WhenExceptionThrown_CancelsHandleLostToken()
    {
        var conn = new FakeDbConnection();

        conn.ExecuteScalarHandler = _ => throw new InvalidOperationException("Oracle connection severed");

        await using var handle = new OracleDistributedLockHandle(
            conn,
            "ora-handle-1",
            "res-keepalive-fail",
            NullLogger.Instance,
            keepaliveCadence: TimeSpan.FromMilliseconds(20));

        var cancelled = handle.HandleLostToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(2));
        cancelled.Should().BeTrue();
        handle.HandleLostToken.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_ReleasesLockAndDisposesConnection()
    {
        var conn = new FakeDbConnection();
        FakeDbCommand? executedCmd = null;

        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            executedCmd = cmd;
            return Task.FromResult(1);
        };

        var handle = new OracleDistributedLockHandle(
            conn,
            "ora-handle-release",
            "res-release",
            NullLogger.Instance,
            keepaliveCadence: TimeSpan.FromMilliseconds(100),
            commandTimeoutSeconds: 50);

        await handle.DisposeAsync();

        executedCmd.Should().NotBeNull();
        executedCmd!.CommandText.Should().Contain("DBMS_LOCK.RELEASE");
        executedCmd.Parameters["LockHandle"].Value.Should().Be("ora-handle-release");
        executedCmd.CommandTimeout.Should().Be(50);
        conn.DisposeAsyncCallCount.Should().Be(1);

        // Idempotency: multiple DisposeAsync calls should not re-execute release
        executedCmd = null;
        await handle.DisposeAsync();
        executedCmd.Should().BeNull();
        conn.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_WhenConnectionClosed_SkipsReleaseAndDisposes()
    {
        var conn = new FakeDbConnection();
        conn.SetState(ConnectionState.Closed);
        FakeDbCommand? executedCmd = null;

        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            executedCmd = cmd;
            return Task.FromResult(1);
        };

        var handle = new OracleDistributedLockHandle(
            conn,
            "ora-handle-closed",
            "res-closed",
            NullLogger.Instance);

        await handle.DisposeAsync();

        executedCmd.Should().BeNull();
        conn.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_WhenReleaseThrowsException_LogsWarningAndDisposesConnection()
    {
        var conn = new FakeDbConnection();

        conn.ExecuteNonQueryAsyncHandler = (_, _) => throw new InvalidOperationException("Oracle ORA-00054: resource busy");

        var handle = new OracleDistributedLockHandle(
            conn,
            "ora-handle-err",
            "res-err",
            NullLogger.Instance);

        var act = () => handle.DisposeAsync().AsTask();
        await act.Should().NotThrowAsync();

        conn.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_DisposesHandleLostCancellationTokenSource()
    {
        var conn = new FakeDbConnection();
        var handle = new OracleDistributedLockHandle(
            conn,
            "ora-handle",
            "res-id",
            NullLogger.Instance);

        var token = handle.HandleLostToken;
        await handle.DisposeAsync();

        var act = () => token.WaitHandle;
        act.Should().Throw<ObjectDisposedException>();
    }
}
