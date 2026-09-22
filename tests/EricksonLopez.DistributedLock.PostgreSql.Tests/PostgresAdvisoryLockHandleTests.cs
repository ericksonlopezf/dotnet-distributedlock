// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EricksonLopez.DistributedLock.PostgreSql.Tests;

public sealed class PostgresAdvisoryLockHandleTests
{
    [Fact]
    public void Constructor_NullConnection_ThrowsArgumentNullException()
    {
        var act = () => new PostgresAdvisoryLockHandle(
            connection: null!,
            lockId: 12345,
            resourceId: "res-1",
            logger: NullLogger.Instance);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("connection");
    }

    [Fact]
    public void Constructor_NullResourceId_ThrowsArgumentNullException()
    {
        using var connection = new FakeDbConnection();
        var act = () => new PostgresAdvisoryLockHandle(
            connection: connection,
            lockId: 12345,
            resourceId: null!,
            logger: NullLogger.Instance);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("resourceId");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        using var connection = new FakeDbConnection();
        var act = () => new PostgresAdvisoryLockHandle(
            connection: connection,
            lockId: 12345,
            resourceId: "res-1",
            logger: null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public void Properties_ReturnAssignedValues()
    {
        using var connection = new FakeDbConnection();
        var handle = new PostgresAdvisoryLockHandle(
            connection: connection,
            lockId: 987654321,
            resourceId: "catalog:sync",
            logger: NullLogger.Instance);

        handle.ResourceId.Should().Be("catalog:sync");
        handle.LockId.Should().Be(987654321);
        handle.HandleLostToken.CanBeCanceled.Should().BeTrue();
        handle.HandleLostToken.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task DisposeAsync_WhenConnectionIsOpen_ExecutesUnlockCommandAndDisposesConnection()
    {
        var connection = new FakeDbConnection();
        var executedSql = string.Empty;
        long capturedLockId = 0;

        connection.ExecuteNonQueryAsyncHandler = (cmd, ct) =>
        {
            executedSql = cmd.CommandText;
            if (cmd.Parameters.Contains("@LockId"))
            {
                capturedLockId = (long)cmd.Parameters["@LockId"].Value!;
            }
            return Task.FromResult(1);
        };

        var handle = new PostgresAdvisoryLockHandle(
            connection: connection,
            lockId: 444555,
            resourceId: "orders:process",
            logger: NullLogger.Instance,
            commandTimeoutSeconds: 15);

        await handle.DisposeAsync();

        executedSql.Should().Be("SELECT pg_advisory_unlock(@LockId);");
        capturedLockId.Should().Be(444555);
        connection.CommandsCreated.Should().ContainSingle(c => c.CommandTimeout == 15);
        connection.DisposeAsyncCallCount.Should().Be(1);
        connection.State.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public async Task DisposeAsync_WhenUnlockThrowsException_CatchesAndDisposesConnectionSafely()
    {
        var connection = new FakeDbConnection();
        connection.ExecuteNonQueryAsyncHandler = (cmd, ct) => throw new InvalidOperationException("DB connection severed");

        var handle = new PostgresAdvisoryLockHandle(
            connection: connection,
            lockId: 111222,
            resourceId: "orders:failing-release",
            logger: NullLogger.Instance);

        var act = async () => await handle.DisposeAsync();
        await act.Should().NotThrowAsync();

        connection.DisposeAsyncCallCount.Should().Be(1);
        connection.State.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public async Task DisposeAsync_WhenConnectionNotOpen_SkipsUnlockAndDisposesConnection()
    {
        var connection = new FakeDbConnection();
        connection.SetState(ConnectionState.Closed);

        var handle = new PostgresAdvisoryLockHandle(
            connection: connection,
            lockId: 777888,
            resourceId: "invoices:closed-conn",
            logger: NullLogger.Instance);

        await handle.DisposeAsync();

        connection.CommandsCreated.Should().BeEmpty();
        connection.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent_ExecutesOnlyOnce()
    {
        var connection = new FakeDbConnection();
        var handle = new PostgresAdvisoryLockHandle(
            connection: connection,
            lockId: 1001,
            resourceId: "idempotency:test",
            logger: NullLogger.Instance);

        await handle.DisposeAsync();
        await handle.DisposeAsync();

        connection.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task RunKeepaliveLoopAsync_WhenConnectionDrops_CancelsHandleLostToken()
    {
        var connection = new FakeDbConnection();
        var handle = new PostgresAdvisoryLockHandle(
            connection: connection,
            lockId: 999111,
            resourceId: "keepalive:connection-drop",
            logger: NullLogger.Instance,
            keepaliveCadence: TimeSpan.FromMilliseconds(20));

        // Simulate connection closing after creation
        connection.SetState(ConnectionState.Closed);

        // Wait for heartbeat loop to detect closed connection
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            while (!handle.HandleLostToken.IsCancellationRequested && !cts.IsCancellationRequested)
            {
                await Task.Delay(10, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout waiting
        }

        handle.HandleLostToken.IsCancellationRequested.Should().BeTrue();

        await handle.DisposeAsync();
    }

    [Fact]
    public async Task RunKeepaliveLoopAsync_WhenHeartbeatThrows_CancelsHandleLostToken()
    {
        var connection = new FakeDbConnection();
        var pingCommandText = string.Empty;
        int? pingCommandTimeout = null;

        connection.ExecuteScalarAsyncHandler = (cmd, ct) =>
        {
            pingCommandText = cmd.CommandText;
            pingCommandTimeout = cmd.CommandTimeout;
            throw new TimeoutException("Database socket failure during ping");
        };

        var handle = new PostgresAdvisoryLockHandle(
            connection: connection,
            lockId: 999222,
            resourceId: "keepalive:ping-throw",
            logger: NullLogger.Instance,
            keepaliveCadence: TimeSpan.FromMilliseconds(20),
            commandTimeoutSeconds: 5);

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            while (!handle.HandleLostToken.IsCancellationRequested && !cts.IsCancellationRequested)
            {
                await Task.Delay(10, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout waiting
        }

        handle.HandleLostToken.IsCancellationRequested.Should().BeTrue();
        pingCommandText.Should().Be("SELECT 1;");
        pingCommandTimeout.Should().Be(5);

        await handle.DisposeAsync();
    }

    [Fact]
    public async Task RunKeepaliveLoopAsync_WhenDisposedPromptly_TerminatesKeepaliveCleanly()
    {
        var connection = new FakeDbConnection();
        connection.ExecuteScalarAsyncHandler = (cmd, ct) => Task.FromResult<object?>(1);

        var handle = new PostgresAdvisoryLockHandle(
            connection: connection,
            lockId: 999333,
            resourceId: "keepalive:clean-disposal",
            logger: NullLogger.Instance,
            keepaliveCadence: TimeSpan.FromMilliseconds(30));

        var token = handle.HandleLostToken;
        await Task.Delay(40);
        await handle.DisposeAsync();

        token.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task RunKeepaliveLoopAsync_WhenHeartbeatQueryThrowsOperationCanceled_BreaksLoopCleanly()
    {
        var connection = new FakeDbConnection();
        using var disposalCts = new CancellationTokenSource();

        connection.ExecuteScalarAsyncHandler = (cmd, ct) =>
        {
            disposalCts.Cancel();
            throw new OperationCanceledException(disposalCts.Token);
        };

        var handle = new PostgresAdvisoryLockHandle(
            connection: connection,
            lockId: 888111,
            resourceId: "keepalive:oce-break",
            logger: NullLogger.Instance,
            keepaliveCadence: TimeSpan.FromMilliseconds(15));

        await Task.Delay(40);
        await handle.DisposeAsync();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task Constructor_WhenKeepaliveCadenceZeroOrNegative_DoesNotStartKeepalive(int ms)
    {
        var connection = new FakeDbConnection();
        var handle = new PostgresAdvisoryLockHandle(
            connection: connection,
            lockId: 1234,
            resourceId: "zero-cadence",
            logger: NullLogger.Instance,
            keepaliveCadence: TimeSpan.FromMilliseconds(ms));

        await Task.Delay(20);
        await handle.DisposeAsync();

        connection.CommandsCreated.Should().OnlyContain(c => c.CommandText.Contains("pg_advisory_unlock"));
    }
}
