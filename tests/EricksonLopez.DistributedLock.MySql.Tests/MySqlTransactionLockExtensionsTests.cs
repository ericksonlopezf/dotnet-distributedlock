// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.MySql;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace EricksonLopez.DistributedLock.MySql.Tests;

public sealed class MySqlTransactionLockExtensionsTests
{
    [Fact]
    public async Task TryAcquireInTransactionAsync_WithDbConnection_ReturnsSuccessWhenCodeOne()
    {
        var conn = new FakeDbConnection();
        FakeDbCommand? capturedCmd = null;

        conn.ExecuteScalarAsyncHandler = (cmd, _) =>
        {
            capturedCmd = cmd;
            return Task.FromResult<object?>(1);
        };

        var tx = new FakeDbTransaction(conn);
        var result = await tx.TryAcquireInTransactionAsync("tenant-orders", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
        result.Value.ResourceId.Should().Be("tenant-orders");
        result.Value.LockId.Should().NotBe(0);
        result.Value.HandleLostToken.Should().Be(CancellationToken.None);

        capturedCmd.Should().NotBeNull();
        capturedCmd!.CommandText.Should().Contain("GET_LOCK");
        capturedCmd.Parameters["@Resource"].Value.Should().Be("tenant-orders");
        capturedCmd.Transaction.Should().Be(tx);

        await result.Value.DisposeAsync();
        var releaseCmd = conn.CommandsCreated.Find(c => c.CommandText.Contains("RELEASE_LOCK"));
        releaseCmd.Should().NotBeNull();
        releaseCmd!.Parameters["@Resource"].Value.Should().Be("tenant-orders");
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_WithDbConnection_ReturnsLockAlreadyHeldWhenCodeNotOne()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(0);

        var tx = new FakeDbTransaction(conn);
        var result = await tx.TryAcquireInTransactionAsync("tenant-orders", NullLogger.Instance);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_WithDbConnection_WhenResultNullOrDBNull_ReturnsLockAlreadyHeld()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(DBNull.Value);

        var tx = new FakeDbTransaction(conn);
        var result = await tx.TryAcquireInTransactionAsync("tenant-orders", NullLogger.Instance);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_WithLongResourceId_NormalizesResourceKey()
    {
        var longKey = new string('x', 100);
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(longKey)));

        var conn = new FakeDbConnection();
        FakeDbCommand? capturedCmd = null;

        conn.ExecuteScalarAsyncHandler = (cmd, _) =>
        {
            capturedCmd = cmd;
            return Task.FromResult<object?>(1);
        };

        var tx = new FakeDbTransaction(conn);
        var result = await tx.TryAcquireInTransactionAsync(longKey, NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
        capturedCmd.Should().NotBeNull();
        capturedCmd!.Parameters["@Resource"].Value.Should().Be(expectedHash);
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_WithExactly64Characters_PreservesKeyIntact()
    {
        var exactKey = new string('k', 64);
        var conn = new FakeDbConnection();
        FakeDbCommand? capturedCmd = null;

        conn.ExecuteScalarAsyncHandler = (cmd, _) =>
        {
            capturedCmd = cmd;
            return Task.FromResult<object?>(1);
        };

        var tx = new FakeDbTransaction(conn);
        var result = await tx.TryAcquireInTransactionAsync(exactKey, NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
        capturedCmd.Should().NotBeNull();
        capturedCmd!.Parameters["@Resource"].Value.Should().Be(exactKey);
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_WithNonDbConnection_ReturnsSuccessWhenCodeOne()
    {
        var syncConn = new FakeSyncConnection();
        FakeSyncCommand? capturedCmd = null;

        syncConn.ExecuteScalarHandler = cmd =>
        {
            capturedCmd = cmd;
            return 1;
        };

        var syncTx = new FakeSyncTransaction(syncConn);
        var result = await ((IDbTransaction)syncTx).TryAcquireInTransactionAsync("sync-resource", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
        result.Value.ResourceId.Should().Be("sync-resource");

        capturedCmd.Should().NotBeNull();
        capturedCmd!.CommandText.Should().Contain("GET_LOCK");
        ((IDbDataParameter)capturedCmd.Parameters["@Resource"]).Value.Should().Be("sync-resource");
        capturedCmd.Transaction.Should().Be(syncTx);

        // Verify sync dispose releases lock
        await result.Value.DisposeAsync();
        var releaseCmd = syncConn.CommandsCreated.Find(c => c.CommandText.Contains("RELEASE_LOCK"));
        releaseCmd.Should().NotBeNull();
        ((IDbDataParameter)releaseCmd!.Parameters["@Resource"]).Value.Should().Be("sync-resource");
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_WithNonDbConnection_ReturnsLockAlreadyHeldWhenCodeNotOne()
    {
        var syncConn = new FakeSyncConnection();
        syncConn.ExecuteScalarHandler = _ => 0;

        var syncTx = new FakeSyncTransaction(syncConn);
        var result = await ((IDbTransaction)syncTx).TryAcquireInTransactionAsync("sync-resource", NullLogger.Instance);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task AcquireInTransactionAsync_WithDbConnection_ReturnsSuccessWhenCodeOne()
    {
        var conn = new FakeDbConnection();
        FakeDbCommand? capturedCmd = null;

        conn.ExecuteScalarAsyncHandler = (cmd, _) =>
        {
            capturedCmd = cmd;
            return Task.FromResult<object?>(1);
        };

        var tx = new FakeDbTransaction(conn);
        var result = await tx.AcquireInTransactionAsync("order-checkout", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
        result.Value.ResourceId.Should().Be("order-checkout");
        capturedCmd.Should().NotBeNull();
        capturedCmd!.CommandText.Should().Contain("GET_LOCK");
        capturedCmd.Parameters["@Resource"].Value.Should().Be("order-checkout");
        capturedCmd.Parameters["@Timeout"].Value.Should().Be(int.MaxValue);
    }

    [Fact]
    public async Task AcquireInTransactionAsync_WithDbConnection_ReturnsLockAlreadyHeldWhenCodeNotOne()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(0);

        var tx = new FakeDbTransaction(conn);
        var result = await tx.AcquireInTransactionAsync("order-checkout", NullLogger.Instance);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task AcquireInTransactionAsync_WithNonDbConnection_ReturnsSuccessWhenCodeOne()
    {
        var syncConn = new FakeSyncConnection();
        FakeSyncCommand? capturedCmd = null;

        syncConn.ExecuteScalarHandler = cmd =>
        {
            capturedCmd = cmd;
            return 1;
        };

        var syncTx = new FakeSyncTransaction(syncConn);
        var result = await ((IDbTransaction)syncTx).AcquireInTransactionAsync("sync-key", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
        capturedCmd.Should().NotBeNull();
        capturedCmd!.CommandText.Should().Contain("GET_LOCK");
        ((IDbDataParameter)capturedCmd.Parameters["@Resource"]).ParameterName.Should().Be("@Resource");
        ((IDbDataParameter)capturedCmd.Parameters["@Resource"]).Value.Should().Be("sync-key");
        ((IDbDataParameter)capturedCmd.Parameters["@Timeout"]).Value.Should().Be(int.MaxValue);
    }

    [Fact]
    public async Task AcquireInTransactionAsync_WithNonDbConnection_ReturnsLockAlreadyHeldWhenCodeNotOne()
    {
        var syncConn = new FakeSyncConnection();
        syncConn.ExecuteScalarHandler = _ => 0;

        var syncTx = new FakeSyncTransaction(syncConn);
        var result = await ((IDbTransaction)syncTx).AcquireInTransactionAsync("sync-key", NullLogger.Instance);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_DbTransactionOverload_ForwardsToImplementation()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(1);

        DbTransaction tx = new FakeDbTransaction(conn);
        var result = await tx.TryAcquireInTransactionAsync("res-db-tx", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AcquireInTransactionAsync_DbTransactionOverload_ForwardsToImplementation()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(1);

        DbTransaction tx = new FakeDbTransaction(conn);
        var result = await tx.AcquireInTransactionAsync("res-db-tx", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task MySqlTransactionLockHandle_DisposeAsync_IsIdempotent()
    {
        var conn = new FakeDbConnection();
        var releaseCount = 0;
        conn.ExecuteNonQueryAsyncHandler = (_, _) =>
        {
            releaseCount++;
            return Task.FromResult(1);
        };
        conn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(1);

        var tx = new FakeDbTransaction(conn);
        var result = await tx.TryAcquireInTransactionAsync("res-idemp", NullLogger.Instance);

        await result.Value.DisposeAsync();
        await result.Value.DisposeAsync();

        releaseCount.Should().Be(1);
    }

    [Fact]
    public async Task MySqlTransactionLockHandle_DisposeAsync_WhenConnectionClosed_SkipsRelease()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(1);

        var tx = new FakeDbTransaction(conn);
        var result = await tx.TryAcquireInTransactionAsync("res-closed", NullLogger.Instance);

        conn.SetState(ConnectionState.Closed);
        conn.CommandsCreated.Clear();

        await result.Value.DisposeAsync();
        conn.CommandsCreated.Should().BeEmpty();
    }

    [Fact]
    public async Task MySqlTransactionLockHandle_DisposeAsync_WhenExceptionThrown_SwallowsGracefully()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(1);
        conn.ExecuteNonQueryAsyncHandler = (_, _) => throw new InvalidOperationException("Release failed");

        var tx = new FakeDbTransaction(conn);
        var result = await tx.TryAcquireInTransactionAsync("res-err", NullLogger.Instance);

        var act = () => result.Value.DisposeAsync().AsTask();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_NullTransaction_ThrowsArgumentNullException()
    {
        IDbTransaction transaction = null!;
        var act = () => transaction.TryAcquireInTransactionAsync("res-1", NullLogger.Instance);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("transaction");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task TryAcquireInTransactionAsync_InvalidResourceId_ThrowsArgumentException(string? resId)
    {
        var transaction = Substitute.For<IDbTransaction>();
        var act = () => transaction.TryAcquireInTransactionAsync(resId!, NullLogger.Instance);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_NullLogger_ThrowsArgumentNullException()
    {
        var conn = new FakeDbConnection();
        var transaction = new FakeDbTransaction(conn);
        var act = () => transaction.TryAcquireInTransactionAsync("res-1", null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_NullConnection_ThrowsInvalidOperationException()
    {
        var transaction = Substitute.For<IDbTransaction>();
        transaction.Connection.Returns((IDbConnection)null!);

        var act = () => transaction.TryAcquireInTransactionAsync("res-1", NullLogger.Instance);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Transaction connection cannot be null.*");
    }

    [Fact]
    public async Task AcquireInTransactionAsync_NullTransaction_ThrowsArgumentNullException()
    {
        IDbTransaction transaction = null!;
        var act = () => transaction.AcquireInTransactionAsync("res-1", NullLogger.Instance);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("transaction");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task AcquireInTransactionAsync_InvalidResourceId_ThrowsArgumentException(string? resId)
    {
        var transaction = Substitute.For<IDbTransaction>();
        var act = () => transaction.AcquireInTransactionAsync(resId!, NullLogger.Instance);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task AcquireInTransactionAsync_NullLogger_ThrowsArgumentNullException()
    {
        var conn = new FakeDbConnection();
        var transaction = new FakeDbTransaction(conn);
        var act = () => transaction.AcquireInTransactionAsync("res-1", null!);
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task AcquireInTransactionAsync_NullConnection_ThrowsInvalidOperationException()
    {
        var transaction = Substitute.For<IDbTransaction>();
        transaction.Connection.Returns((IDbConnection)null!);

        var act = () => transaction.AcquireInTransactionAsync("res-1", NullLogger.Instance);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Transaction connection cannot be null.*");
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_WithNonDbConnection_WhenResultIsDBNull_ReturnsLockAlreadyHeld()
    {
        var conn = new FakeSyncConnection();
        conn.ExecuteScalarHandler = _ => DBNull.Value;
        var tx = new FakeSyncTransaction(conn);

        var result = await tx.TryAcquireInTransactionAsync("tenant-orders", NullLogger.Instance);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task AcquireInTransactionAsync_WithDbConnection_WhenResultIsDBNull_ReturnsLockAlreadyHeld()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(DBNull.Value);
        var tx = new FakeDbTransaction(conn);

        var result = await tx.AcquireInTransactionAsync("tenant-orders", NullLogger.Instance);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task AcquireInTransactionAsync_WithNonDbConnection_WhenResultIsDBNull_ReturnsLockAlreadyHeld()
    {
        var conn = new FakeSyncConnection();
        conn.ExecuteScalarHandler = _ => DBNull.Value;
        var tx = new FakeSyncTransaction(conn);

        var result = await tx.AcquireInTransactionAsync("tenant-orders", NullLogger.Instance);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task MySqlTransactionLockHandle_DisposeAsync_WithNonDbConnection_ExecutesReleaseLock()
    {
        var conn = new FakeSyncConnection();
        var executed = false;
        conn.ExecuteNonQueryHandler = cmd =>
        {
            if (cmd.CommandText.Contains("RELEASE_LOCK"))
            {
                executed = true;
            }
            return 1;
        };
        conn.ExecuteScalarHandler = _ => 1;
        var tx = new FakeSyncTransaction(conn);

        var result = await tx.TryAcquireInTransactionAsync("res-sync-dispose", NullLogger.Instance);
        result.IsSuccess.Should().BeTrue();

        await result.Value.DisposeAsync();

        executed.Should().BeTrue();
    }
}
