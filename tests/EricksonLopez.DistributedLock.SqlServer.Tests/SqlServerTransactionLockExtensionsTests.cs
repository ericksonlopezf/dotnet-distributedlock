// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.SqlServer;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace EricksonLopez.DistributedLock.SqlServer.Tests;

public sealed class SqlServerTransactionLockExtensionsTests
{
    [Fact]
    public async Task TryAcquireInTransactionAsync_WithDbConnection_ReturnsSuccessWhenCodeZeroOrPositive()
    {
        var conn = new FakeDbConnection();
        FakeDbCommand? capturedCmd = null;

        conn.ExecuteScalarAsyncHandler = (cmd, _) =>
        {
            capturedCmd = cmd;
            return Task.FromResult<object?>(0);
        };

        var tx = new FakeDbTransaction(conn);
        var result = await tx.TryAcquireInTransactionAsync("tenant-orders", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
        result.Value.ResourceId.Should().Be("tenant-orders");
        result.Value.LockId.Should().NotBe(0);
        result.Value.HandleLostToken.Should().Be(CancellationToken.None);
        await result.Value.DisposeAsync();

        capturedCmd.Should().NotBeNull();
        capturedCmd!.CommandText.Should().Contain("sys.sp_getapplock");
        capturedCmd.Parameters["@Resource"].Value.Should().Be("tenant-orders");
        capturedCmd.Transaction.Should().Be(tx);
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_WithDbConnection_ReturnsLockAlreadyHeldWhenCodeNegative()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(-1);

        var tx = new FakeDbTransaction(conn);
        var result = await tx.TryAcquireInTransactionAsync("tenant-orders", NullLogger.Instance);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_WithLongResourceId_NormalizesResourceKey()
    {
        var longKey = new string('x', 300);
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(longKey)));

        var conn = new FakeDbConnection();
        FakeDbCommand? capturedCmd = null;

        conn.ExecuteScalarAsyncHandler = (cmd, _) =>
        {
            capturedCmd = cmd;
            return Task.FromResult<object?>(0);
        };

        var tx = new FakeDbTransaction(conn);
        var result = await tx.TryAcquireInTransactionAsync(longKey, NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
        capturedCmd.Should().NotBeNull();
        capturedCmd!.Parameters["@Resource"].Value.Should().Be(expectedHash);
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_WithNonDbConnection_ReturnsSuccessWhenCodeZeroOrPositive()
    {
        var syncConn = new FakeSyncConnection();
        FakeSyncCommand? capturedCmd = null;

        syncConn.ExecuteScalarHandler = cmd =>
        {
            capturedCmd = cmd;
            return 0;
        };

        var syncTx = new FakeSyncTransaction(syncConn);
        var result = await ((IDbTransaction)syncTx).TryAcquireInTransactionAsync("sync-resource", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
        result.Value.ResourceId.Should().Be("sync-resource");

        capturedCmd.Should().NotBeNull();
        capturedCmd!.CommandText.Should().Contain("sys.sp_getapplock");
        ((IDbDataParameter)capturedCmd.Parameters["@Resource"]).Value.Should().Be("sync-resource");
        capturedCmd.Transaction.Should().Be(syncTx);
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_WithNonDbConnection_ReturnsLockAlreadyHeldWhenCodeNegative()
    {
        var syncConn = new FakeSyncConnection();
        syncConn.ExecuteScalarHandler = _ => -1;

        var syncTx = new FakeSyncTransaction(syncConn);
        var result = await ((IDbTransaction)syncTx).TryAcquireInTransactionAsync("sync-resource", NullLogger.Instance);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task AcquireInTransactionAsync_WithDbConnection_ReturnsSuccessWhenCodeZeroOrPositive()
    {
        var conn = new FakeDbConnection();
        FakeDbCommand? capturedCmd = null;

        conn.ExecuteScalarAsyncHandler = (cmd, _) =>
        {
            capturedCmd = cmd;
            return Task.FromResult<object?>(0);
        };

        var tx = new FakeDbTransaction(conn);
        var result = await tx.AcquireInTransactionAsync("order-checkout", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
        result.Value.ResourceId.Should().Be("order-checkout");
        capturedCmd.Should().NotBeNull();
        capturedCmd!.CommandText.Should().Contain("sys.sp_getapplock");
        capturedCmd.Parameters["@Resource"].Value.Should().Be("order-checkout");
    }

    [Fact]
    public async Task AcquireInTransactionAsync_WithDbConnection_ReturnsLockAlreadyHeldWhenCodeNegative()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(-999);

        var tx = new FakeDbTransaction(conn);
        var result = await tx.AcquireInTransactionAsync("order-checkout", NullLogger.Instance);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task AcquireInTransactionAsync_WithNonDbConnection_ReturnsSuccessWhenCodeZeroOrPositive()
    {
        var syncConn = new FakeSyncConnection();
        FakeSyncCommand? capturedCmd = null;

        syncConn.ExecuteScalarHandler = cmd =>
        {
            capturedCmd = cmd;
            return 0;
        };

        var syncTx = new FakeSyncTransaction(syncConn);
        var result = await ((IDbTransaction)syncTx).AcquireInTransactionAsync("sync-key", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
        capturedCmd.Should().NotBeNull();
        capturedCmd!.CommandText.Should().Contain("sys.sp_getapplock");
        ((IDbDataParameter)capturedCmd.Parameters["@Resource"]).ParameterName.Should().Be("@Resource");
        ((IDbDataParameter)capturedCmd.Parameters["@Resource"]).Value.Should().Be("sync-key");
    }

    [Fact]
    public async Task AcquireInTransactionAsync_WithNonDbConnection_ReturnsLockAlreadyHeldWhenCodeNegative()
    {
        var syncConn = new FakeSyncConnection();
        syncConn.ExecuteScalarHandler = _ => -1;

        var syncTx = new FakeSyncTransaction(syncConn);
        var result = await ((IDbTransaction)syncTx).AcquireInTransactionAsync("sync-key", NullLogger.Instance);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_DbTransactionOverload_ForwardsToImplementation()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(0);

        DbTransaction tx = new FakeDbTransaction(conn);
        var result = await tx.TryAcquireInTransactionAsync("res-db-tx", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AcquireInTransactionAsync_DbTransactionOverload_ForwardsToImplementation()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(0);

        DbTransaction tx = new FakeDbTransaction(conn);
        var result = await tx.AcquireInTransactionAsync("res-db-tx", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
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
    public async Task TryAcquireInTransactionAsync_WithExactly255Characters_PreservesKeyIntact()
    {
        var exactKey = new string('k', 255);
        var conn = new FakeDbConnection();
        FakeDbCommand? capturedCmd = null;

        conn.ExecuteScalarAsyncHandler = (cmd, _) =>
        {
            capturedCmd = cmd;
            return Task.FromResult<object?>(0);
        };

        var tx = new FakeDbTransaction(conn);
        var result = await tx.TryAcquireInTransactionAsync(exactKey, NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
        capturedCmd.Should().NotBeNull();
        capturedCmd!.Parameters["@Resource"].Value.Should().Be(exactKey);
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
}
