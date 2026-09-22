// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Oracle;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace EricksonLopez.DistributedLock.Oracle.Tests;

public sealed class OracleTransactionLockExtensionsTests
{
    [Fact]
    public async Task TryAcquireInTransactionAsync_WithDbConnection_ReturnsSuccessWhenStatusZero()
    {
        var conn = new FakeDbConnection();
        FakeDbCommand? executedCmd = null;

        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            executedCmd = cmd;
            cmd.Parameters["OutStatus"].Value = 0;
            return Task.FromResult(1);
        };

        var tx = new FakeDbTransaction(conn);
        var result = await tx.TryAcquireInTransactionAsync("tenant-invoice-100", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.ResourceId.Should().Be("tenant-invoice-100");
        result.Value.HandleLostToken.Should().Be(CancellationToken.None);

        executedCmd.Should().NotBeNull();
        executedCmd!.CommandText.Should().Contain("DBMS_LOCK.ALLOCATE_UNIQUE");
        executedCmd.Parameters["LockName"].Value.Should().Be("tenant-invoice-100");

        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_WithDbConnection_ReturnsLockAlreadyHeldWhenStatusNotZero()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            cmd.Parameters["OutStatus"].Value = 1; // Timeout / Already held
            return Task.FromResult(1);
        };

        var tx = new FakeDbTransaction(conn);
        var result = await tx.TryAcquireInTransactionAsync("tenant-invoice-100", NullLogger.Instance);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_WithLongResourceId_NormalizesResourceKey()
    {
        var longKey = new string('o', 150);
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(longKey)));

        var conn = new FakeDbConnection();
        FakeDbCommand? executedCmd = null;

        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            executedCmd = cmd;
            cmd.Parameters["OutStatus"].Value = 0;
            return Task.FromResult(1);
        };

        var tx = new FakeDbTransaction(conn);
        var result = await tx.TryAcquireInTransactionAsync(longKey, NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
        executedCmd.Should().NotBeNull();
        executedCmd!.Parameters["LockName"].Value.Should().Be(expectedHash);
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_WithExactly128Characters_PreservesKeyIntact()
    {
        var exactKey = new string('k', 128);
        var conn = new FakeDbConnection();
        FakeDbCommand? executedCmd = null;

        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            executedCmd = cmd;
            cmd.Parameters["OutStatus"].Value = 0;
            return Task.FromResult(1);
        };

        var tx = new FakeDbTransaction(conn);
        var result = await tx.TryAcquireInTransactionAsync(exactKey, NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
        executedCmd.Should().NotBeNull();
        executedCmd!.Parameters["LockName"].Value.Should().Be(exactKey);
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_WithNonDbConnection_ReturnsSuccessWhenStatusZero()
    {
        var syncConn = new FakeSyncConnection();
        FakeSyncCommand? executedCmd = null;

        syncConn.ExecuteNonQueryHandler = cmd =>
        {
            executedCmd = cmd;
            ((IDbDataParameter)cmd.Parameters["OutStatus"]).Value = 0;
            return 1;
        };

        var tx = new FakeSyncTransaction(syncConn);
        var result = await tx.TryAcquireInTransactionAsync("sync-invoice-100", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
        result.Value.ResourceId.Should().Be("sync-invoice-100");
        executedCmd.Should().NotBeNull();
        executedCmd!.CommandText.Should().Contain("DBMS_LOCK.ALLOCATE_UNIQUE");
        ((IDbDataParameter)executedCmd.Parameters["LockName"]).Value.Should().Be("sync-invoice-100");
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_WithNonDbConnection_ReturnsLockAlreadyHeldWhenStatusNotZero()
    {
        var syncConn = new FakeSyncConnection();
        syncConn.ExecuteNonQueryHandler = cmd =>
        {
            ((IDbDataParameter)cmd.Parameters["OutStatus"]).Value = 1;
            return 1;
        };

        var tx = new FakeSyncTransaction(syncConn);
        var result = await tx.TryAcquireInTransactionAsync("sync-invoice-100", NullLogger.Instance);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task AcquireInTransactionAsync_WithDbConnection_ReturnsSuccessWhenStatusZero()
    {
        var conn = new FakeDbConnection();
        FakeDbCommand? executedCmd = null;

        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            executedCmd = cmd;
            cmd.Parameters["OutStatus"].Value = 0;
            return Task.FromResult(1);
        };

        var tx = new FakeDbTransaction(conn);
        var result = await tx.AcquireInTransactionAsync("acq-invoice-100", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
        executedCmd.Should().NotBeNull();
        executedCmd!.CommandText.Should().Contain("DBMS_LOCK.ALLOCATE_UNIQUE");
        executedCmd!.Parameters["LockName"].Value.Should().Be("acq-invoice-100");
        executedCmd!.Parameters["Timeout"].Value.Should().Be(32767);
    }

    [Fact]
    public async Task AcquireInTransactionAsync_WithDbConnection_ReturnsLockAlreadyHeldWhenStatusNotZero()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            cmd.Parameters["OutStatus"].Value = 2; // Deadlock / Busy
            return Task.FromResult(1);
        };

        var tx = new FakeDbTransaction(conn);
        var result = await tx.AcquireInTransactionAsync("acq-invoice-100", NullLogger.Instance);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task AcquireInTransactionAsync_WithNonDbConnection_ReturnsSuccessWhenStatusZero()
    {
        var syncConn = new FakeSyncConnection();
        FakeSyncCommand? executedCmd = null;

        syncConn.ExecuteNonQueryHandler = cmd =>
        {
            executedCmd = cmd;
            ((IDbDataParameter)cmd.Parameters["OutStatus"]).Value = 0;
            return 1;
        };

        var tx = new FakeSyncTransaction(syncConn);
        var result = await tx.AcquireInTransactionAsync("sync-acq-invoice", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
        executedCmd.Should().NotBeNull();
        executedCmd!.CommandText.Should().Contain("DBMS_LOCK.ALLOCATE_UNIQUE");
        ((IDbDataParameter)executedCmd!.Parameters["LockName"]).Value.Should().Be("sync-acq-invoice");
        ((IDbDataParameter)executedCmd!.Parameters["Timeout"]).Value.Should().Be(32767);
    }

    [Fact]
    public async Task AcquireInTransactionAsync_WithNonDbConnection_ReturnsLockAlreadyHeldWhenStatusNotZero()
    {
        var syncConn = new FakeSyncConnection();
        syncConn.ExecuteNonQueryHandler = cmd =>
        {
            ((IDbDataParameter)cmd.Parameters["OutStatus"]).Value = 1;
            return 1;
        };

        var tx = new FakeSyncTransaction(syncConn);
        var result = await tx.AcquireInTransactionAsync("sync-acq-invoice", NullLogger.Instance);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_DbTransactionOverload_ForwardsToImplementation()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            cmd.Parameters["OutStatus"].Value = 0;
            return Task.FromResult(1);
        };

        DbTransaction tx = new FakeDbTransaction(conn);
        var result = await tx.TryAcquireInTransactionAsync("res-typed", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AcquireInTransactionAsync_DbTransactionOverload_ForwardsToImplementation()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            cmd.Parameters["OutStatus"].Value = 0;
            return Task.FromResult(1);
        };

        DbTransaction tx = new FakeDbTransaction(conn);
        var result = await tx.AcquireInTransactionAsync("res-typed-acq", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
    }

    #region Guards

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

    #endregion
}
