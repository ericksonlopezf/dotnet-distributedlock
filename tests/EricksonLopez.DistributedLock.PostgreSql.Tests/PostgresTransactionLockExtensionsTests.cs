// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.PostgreSql;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace EricksonLopez.DistributedLock.PostgreSql.Tests;

public sealed class PostgresTransactionLockExtensionsTests
{
    [Fact]
    public async Task TryAcquireInTransactionAsync_NullTransaction_ThrowsArgumentNullException()
    {
        IDbTransaction transaction = null!;
        var act = () => transaction.TryAcquireInTransactionAsync("res-1", NullLogger.Instance);
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("transaction");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
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
        var connection = new FakeDbConnection();
        var transaction = new FakeDbTransaction(connection);
        var act = () => transaction.TryAcquireInTransactionAsync("res-1", null!);
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_NullConnection_ThrowsInvalidOperationException()
    {
        var transaction = Substitute.For<IDbTransaction>();
        transaction.Connection.Returns((IDbConnection)null!);

        var act = () => transaction.TryAcquireInTransactionAsync("res-1", NullLogger.Instance);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*connection is null or closed*");
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_DbTransaction_WhenAcquired_ReturnsSuccess()
    {
        var connection = new FakeDbConnection();
        var transaction = new FakeDbTransaction(connection);
        var executedSql = string.Empty;
        long capturedLockId = 0;

        connection.ExecuteScalarAsyncHandler = (cmd, ct) =>
        {
            executedSql = cmd.CommandText;
            capturedLockId = (long)cmd.Parameters["@LockId"].Value!;
            return Task.FromResult<object?>(true);
        };

        var result = await transaction.TryAcquireInTransactionAsync("invoice:xact-1", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<NoOpAsyncDisposable>();
        result.Value.ResourceId.Should().Be("invoice:xact-1");
        executedSql.Should().Be("SELECT pg_try_advisory_xact_lock(@LockId);");
        capturedLockId.Should().Be(PostgresDistributedLockProvider.GenerateLockId("invoice:xact-1"));
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_DbTransaction_WhenAlreadyHeld_ReturnsLockAlreadyHeld()
    {
        var connection = new FakeDbConnection();
        var transaction = new FakeDbTransaction(connection);

        connection.ExecuteScalarAsyncHandler = (cmd, ct) => Task.FromResult<object?>(false);

        var result = await transaction.TryAcquireInTransactionAsync("invoice:xact-held", NullLogger.Instance);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_DbTransaction_WhenCanceled_ReturnsCanceled()
    {
        var connection = new FakeDbConnection();
        var transaction = new FakeDbTransaction(connection);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        connection.ExecuteScalarAsyncHandler = (cmd, ct) => throw new OperationCanceledException(ct);

        var result = await transaction.TryAcquireInTransactionAsync("invoice:xact-cancel", NullLogger.Instance, cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_SyncTransaction_WhenAcquired_ReturnsSuccess()
    {
        var syncConn = new FakeSyncConnection();
        var syncTran = new FakeSyncTransaction(syncConn);
        var executedSql = string.Empty;
        long capturedLockId = 0;

        syncConn.ExecuteScalarHandler = cmd =>
        {
            executedSql = cmd.CommandText;
            var param = (IDbDataParameter)cmd.Parameters["@LockId"];
            capturedLockId = (long)param.Value!;
            return true;
        };

        var result = await syncTran.TryAcquireInTransactionAsync("order:sync-xact", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<NoOpAsyncDisposable>();
        executedSql.Should().Be("SELECT pg_try_advisory_xact_lock(@LockId);");
        capturedLockId.Should().Be(PostgresDistributedLockProvider.GenerateLockId("order:sync-xact"));
    }

    [Fact]
    public async Task TryAcquireInTransactionAsync_SyncTransaction_WhenAlreadyHeld_ReturnsLockAlreadyHeld()
    {
        var syncConn = new FakeSyncConnection();
        var syncTran = new FakeSyncTransaction(syncConn);
        syncConn.ExecuteScalarHandler = cmd => false;

        var result = await syncTran.TryAcquireInTransactionAsync("order:sync-held", NullLogger.Instance);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task AcquireInTransactionAsync_NullTransaction_ThrowsArgumentNullException()
    {
        IDbTransaction transaction = null!;
        var act = () => transaction.AcquireInTransactionAsync("res-1", NullLogger.Instance);
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("transaction");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
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
        var connection = new FakeDbConnection();
        var transaction = new FakeDbTransaction(connection);
        var act = () => transaction.AcquireInTransactionAsync("res-1", null!);
        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public async Task AcquireInTransactionAsync_NullConnection_ThrowsInvalidOperationException()
    {
        var transaction = Substitute.For<IDbTransaction>();
        transaction.Connection.Returns((IDbConnection)null!);

        var act = () => transaction.AcquireInTransactionAsync("res-1", NullLogger.Instance);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*connection is null or closed*");
    }

    [Fact]
    public async Task AcquireInTransactionAsync_DbTransaction_WhenAcquired_ReturnsSuccess()
    {
        var connection = new FakeDbConnection();
        var transaction = new FakeDbTransaction(connection);
        var executedSql = string.Empty;
        long capturedLockId = 0;

        connection.ExecuteNonQueryAsyncHandler = (cmd, ct) =>
        {
            executedSql = cmd.CommandText;
            capturedLockId = (long)cmd.Parameters["@LockId"].Value!;
            return Task.FromResult(1);
        };

        var result = await transaction.AcquireInTransactionAsync("invoice:blocking-xact", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<NoOpAsyncDisposable>();
        executedSql.Should().Be("SELECT pg_advisory_xact_lock(@LockId);");
        capturedLockId.Should().Be(PostgresDistributedLockProvider.GenerateLockId("invoice:blocking-xact"));
    }

    [Fact]
    public async Task AcquireInTransactionAsync_DbTransaction_WhenCanceled_ReturnsCanceled()
    {
        var connection = new FakeDbConnection();
        var transaction = new FakeDbTransaction(connection);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        connection.ExecuteNonQueryAsyncHandler = (cmd, ct) => throw new OperationCanceledException(ct);

        var result = await transaction.AcquireInTransactionAsync("invoice:blocking-cancel", NullLogger.Instance, cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task AcquireInTransactionAsync_SyncTransaction_WhenAcquired_ReturnsSuccess()
    {
        var syncConn = new FakeSyncConnection();
        var syncTran = new FakeSyncTransaction(syncConn);
        var executedSql = string.Empty;
        long capturedLockId = 0;

        syncConn.ExecuteNonQueryHandler = cmd =>
        {
            executedSql = cmd.CommandText;
            var param = (IDbDataParameter)cmd.Parameters["@LockId"];
            capturedLockId = (long)param.Value!;
            return 1;
        };

        var result = await syncTran.AcquireInTransactionAsync("order:sync-blocking", NullLogger.Instance);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<NoOpAsyncDisposable>();
        executedSql.Should().Be("SELECT pg_advisory_xact_lock(@LockId);");
        capturedLockId.Should().Be(PostgresDistributedLockProvider.GenerateLockId("order:sync-blocking"));
    }
}
