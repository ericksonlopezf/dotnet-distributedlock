// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Sqlite;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EricksonLopez.DistributedLock.Sqlite.Tests;

public sealed class SqliteTransactionLockExtensionsTests
{
    #region Argument Validation

    [Fact]
    public async Task TryAcquireInTransactionAsync_NullTransaction_ThrowsArgumentNullException()
    {
        IDbTransaction transaction = null!;
        var act = () => transaction.TryAcquireInTransactionAsync("res-1", NullLogger.Instance);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("transaction");
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_NullDbTransaction_ThrowsArgumentNullException()
    {
        DbTransaction transaction = null!;
        var act = () => transaction.TryAcquireInTransactionAsync("res-1", NullLogger.Instance);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("transaction");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task TryAcquireInTransactionAsync_InvalidResourceId_ThrowsArgumentException(string? resId)
    {
        var conn = new FakeDbConnection();
        var tx = new FakeDbTransaction(conn);
        var act = () => tx.TryAcquireInTransactionAsync(resId!, NullLogger.Instance);
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("resourceId");
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_NullLogger_ThrowsArgumentNullException()
    {
        var conn = new FakeDbConnection();
        var tx = new FakeDbTransaction(conn);
        var act = () => tx.TryAcquireInTransactionAsync("res-1", null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_NullConnection_ThrowsInvalidOperationException()
    {
        var tx = new FakeSyncTransaction(null!);
        var act = () => tx.TryAcquireInTransactionAsync("res-1", NullLogger.Instance);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Transaction connection cannot be null.*");
    }

    [Fact]
    public async Task AcquireInTransactionAsync_NullTransaction_ThrowsArgumentNullException()
    {
        IDbTransaction transaction = null!;
        var act = () => transaction.AcquireInTransactionAsync("res-1", NullLogger.Instance);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("transaction");
    }

    [Fact]
    public async Task AcquireInTransactionAsync_NullDbTransaction_ThrowsArgumentNullException()
    {
        DbTransaction transaction = null!;
        var act = () => transaction.AcquireInTransactionAsync("res-1", NullLogger.Instance);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("transaction");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task AcquireInTransactionAsync_InvalidResourceId_ThrowsArgumentException(string? resId)
    {
        var conn = new FakeDbConnection();
        var tx = new FakeDbTransaction(conn);
        var act = () => tx.AcquireInTransactionAsync(resId!, NullLogger.Instance);
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("resourceId");
    }

    #endregion

    #region DbConnection (Async) Path

    [Fact]
    public async Task TryAcquireInTransactionAsync_WithDbConnection_Success_CreatesTableAndInserts()
    {
        var conn = new FakeDbConnection();
        var commands = new System.Collections.Generic.List<FakeDbCommand>();

        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            commands.Add(cmd);
            return Task.FromResult(1);
        };

        var tx = new FakeDbTransaction(conn);
        var result = await tx.TryAcquireInTransactionAsync("tx-invoice-1", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
        result.Value.ResourceId.Should().Be("tx-invoice-1");
        result.Value.HandleLostToken.Should().Be(CancellationToken.None);

        commands.Should().HaveCount(2);
        commands[0].CommandText.Should().Contain("CREATE TABLE IF NOT EXISTS __distributed_locks");
        commands[1].CommandText.Should().Contain("INSERT INTO __distributed_locks");
        commands[1].Parameters["@ResourceId"]!.ParameterName.Should().Be("@ResourceId");
        commands[1].Parameters["@ResourceId"]!.Value.Should().Be("tx-invoice-1");
        commands[1].Parameters["@OwnerId"]!.ParameterName.Should().Be("@OwnerId");
        commands[1].Parameters["@OwnerId"]!.Value!.ToString()!.Length.Should().Be(32);

        // Dispose handle
        await result.Value.DisposeAsync();
        commands.Should().HaveCount(3);
        commands[2].CommandText.Should().Contain("DELETE FROM __distributed_locks");
        commands[2].Parameters["@ResourceId"]!.ParameterName.Should().Be("@ResourceId");
        commands[2].Parameters["@ResourceId"]!.Value.Should().Be("tx-invoice-1");
        commands[2].Parameters["@OwnerId"]!.ParameterName.Should().Be("@OwnerId");
        commands[2].Parameters["@OwnerId"]!.Value!.ToString()!.Length.Should().Be(32);

        // Idempotent
        await result.Value.DisposeAsync();
        commands.Should().HaveCount(3);
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_WithDbConnection_ConstraintError_ReturnsLockAlreadyHeld()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            if (cmd.CommandText.Contains("INSERT INTO"))
            {
                throw new SqliteException("Constraint error", 19);
            }
            return Task.FromResult(1);
        };

        var tx = new FakeDbTransaction(conn);
        var result = await tx.TryAcquireInTransactionAsync("tx-held-res", NullLogger.Instance);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_DbTransactionOverload_Success()
    {
        var conn = new FakeDbConnection();
        DbTransaction tx = new FakeDbTransaction(conn);

        var result = await tx.TryAcquireInTransactionAsync("tx-db-overload", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
        result.Value.ResourceId.Should().Be("tx-db-overload");
    }

    #endregion

    #region Non-DbConnection (Sync) Path

    [Fact]
    public async Task TryAcquireInTransactionAsync_WithNonDbConnection_Success_ExecutesSyncCommands()
    {
        var syncConn = new FakeSyncConnection();
        var commands = new System.Collections.Generic.List<FakeSyncCommand>();

        syncConn.ExecuteNonQueryHandler = cmd =>
        {
            commands.Add(cmd);
            return 1;
        };

        var tx = new FakeSyncTransaction(syncConn);
        var result = await tx.TryAcquireInTransactionAsync("sync-tx-res", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
        result.Value.ResourceId.Should().Be("sync-tx-res");

        commands.Should().HaveCount(2);
        commands[0].CommandText.Should().Contain("CREATE TABLE IF NOT EXISTS __distributed_locks");
        commands[1].CommandText.Should().Contain("INSERT INTO __distributed_locks");
        ((IDbDataParameter)commands[1].Parameters["@ResourceId"]!).ParameterName.Should().Be("@ResourceId");
        ((IDbDataParameter)commands[1].Parameters["@ResourceId"]!).Value.Should().Be("sync-tx-res");
        ((IDbDataParameter)commands[1].Parameters["@OwnerId"]!).ParameterName.Should().Be("@OwnerId");
        ((IDbDataParameter)commands[1].Parameters["@OwnerId"]!).Value!.ToString()!.Length.Should().Be(32);

        // Dispose sync handle: should execute DELETE synchronously
        await result.Value.DisposeAsync();
        commands.Should().HaveCount(3);
        commands[2].CommandText.Should().Contain("DELETE FROM __distributed_locks");
        ((IDbDataParameter)commands[2].Parameters["@ResourceId"]!).ParameterName.Should().Be("@ResourceId");
        ((IDbDataParameter)commands[2].Parameters["@ResourceId"]!).Value.Should().Be("sync-tx-res");
        ((IDbDataParameter)commands[2].Parameters["@OwnerId"]!).ParameterName.Should().Be("@OwnerId");
        ((IDbDataParameter)commands[2].Parameters["@OwnerId"]!).Value!.ToString()!.Length.Should().Be(32);
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_WithNonDbConnection_ConstraintError_ReturnsLockAlreadyHeld()
    {
        var syncConn = new FakeSyncConnection();
        syncConn.ExecuteNonQueryHandler = cmd =>
        {
            if (cmd.CommandText.Contains("INSERT INTO"))
            {
                throw new SqliteException("Constraint error", 19);
            }
            return 1;
        };

        var tx = new FakeSyncTransaction(syncConn);
        var result = await tx.TryAcquireInTransactionAsync("sync-tx-busy", NullLogger.Instance);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    #endregion

    #region Transaction Lock Handle Dispose Edge Cases

    [Fact]
    public async Task TransactionLockHandle_DisposeAsync_WhenConnectionClosed_SkipsDelete()
    {
        var conn = new FakeDbConnection();
        var commands = new System.Collections.Generic.List<FakeDbCommand>();

        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            commands.Add(cmd);
            return Task.FromResult(1);
        };

        var tx = new FakeDbTransaction(conn);
        var result = await tx.TryAcquireInTransactionAsync("closed-conn-tx", NullLogger.Instance);
        result.IsSuccess.Should().BeTrue();

        conn.SetState(ConnectionState.Closed);
        await result.Value.DisposeAsync();

        // No DELETE command should have executed
        commands.Should().HaveCount(2);
    }

    [Fact]
    public async Task TransactionLockHandle_DisposeAsync_WhenExceptionThrown_LogsWarningAndDoesNotThrow()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            if (cmd.CommandText.Contains("DELETE FROM"))
            {
                throw new InvalidOperationException("Failed to delete record");
            }
            return Task.FromResult(1);
        };

        var tx = new FakeDbTransaction(conn);
        var result = await tx.TryAcquireInTransactionAsync("err-tx", NullLogger.Instance);
        result.IsSuccess.Should().BeTrue();

        var act = () => result.Value.DisposeAsync().AsTask();
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Blocking AcquireInTransactionAsync Tests

    [Fact]
    public async Task AcquireInTransactionAsync_ImmediateSuccess_ReturnsHandle()
    {
        var conn = new FakeDbConnection();
        var tx = new FakeDbTransaction(conn);

        var result = await tx.AcquireInTransactionAsync("acq-tx-succ", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AcquireInTransactionAsync_PreCanceled_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var conn = new FakeDbConnection();
        var tx = new FakeDbTransaction(conn);

        var act = () => tx.AcquireInTransactionAsync("acq-tx-cancel", NullLogger.Instance, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AcquireInTransactionAsync_RetriesUntilSuccess()
    {
        var attempts = 0;
        var conn = new FakeDbConnection();
        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            if (cmd.CommandText.Contains("INSERT INTO"))
            {
                attempts++;
                if (attempts < 2)
                {
                    throw new SqliteException("busy", 19);
                }
            }
            return Task.FromResult(1);
        };

        var tx = new FakeDbTransaction(conn);
        var result = await tx.AcquireInTransactionAsync("acq-tx-retry", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task AcquireInTransactionAsync_DbTransactionOverload_Success()
    {
        var conn = new FakeDbConnection();
        DbTransaction tx = new FakeDbTransaction(conn);

        var result = await tx.AcquireInTransactionAsync("acq-tx-db-overload", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
    }

    #endregion
}
