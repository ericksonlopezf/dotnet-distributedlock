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
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.DistributedLock.SqlServer.Tests;

public sealed class SqlServerDistributedLockProviderTests
{
    private readonly NullLogger<SqlServerDistributedLockProvider> _logger = NullLogger<SqlServerDistributedLockProvider>.Instance;

    [Fact]
    public void Constructor_WithConnection_NullConnection_ThrowsArgumentNullException()
    {
        var act = () => new SqlServerDistributedLockProvider((IDbConnection)null!, _logger);
        act.Should().Throw<ArgumentNullException>().WithParameterName("connection");
    }

    [Fact]
    public void Constructor_WithConnection_NullLogger_ThrowsArgumentNullException()
    {
        var conn = new FakeDbConnection();
        var act = () => new SqlServerDistributedLockProvider(conn, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithFactory_NullFactory_ThrowsArgumentNullException()
    {
        var act = () => new SqlServerDistributedLockProvider((Func<DbConnection>)null!, _logger);
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_WithFactory_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new SqlServerDistributedLockProvider(() => new FakeDbConnection(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithOptionsWrapper_InitializesCorrectly()
    {
        var options = Options.Create(new SqlServerLockOptions { CommandTimeoutSeconds = 40 });
        var provider = new SqlServerDistributedLockProvider(() => new FakeDbConnection(), _logger, options);
        provider.Should().NotBeNull();
    }

    #region Session Lock (Connection Factory)

    [Fact]
    public async Task TryAcquireHandleAsync_SessionMode_Success_OpensClosedConnectionAndReturnsHandle()
    {
        FakeDbConnection? createdConn = null;
        FakeDbCommand? executedCmd = null;

        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.SetState(ConnectionState.Closed);
                createdConn.ExecuteScalarAsyncHandler = (cmd, _) =>
                {
                    executedCmd = cmd;
                    return Task.FromResult<object?>(0);
                };
                return createdConn;
            },
            _logger,
            new SqlServerLockOptions { CommandTimeoutSeconds = 20 });

        var result = await provider.TryAcquireHandleAsync("order-invoice");

        result.IsSuccess.Should().BeTrue();
        result.Value.ResourceId.Should().Be("order-invoice");

        createdConn.Should().NotBeNull();
        createdConn!.OpenAsyncCallCount.Should().Be(1);

        executedCmd.Should().NotBeNull();
        executedCmd!.CommandText.Should().Contain("sys.sp_getapplock");
        executedCmd.Parameters["@Resource"].Value.Should().Be("order-invoice");
        executedCmd.Parameters["@LockOwner"].Value.Should().Be("Session");
        executedCmd.Parameters["@LockTimeout"].Value.Should().Be(0);
        executedCmd.CommandTimeout.Should().Be(20);

        await result.Value.DisposeAsync();
        createdConn.DisposeAsyncCallCount.Should().Be(1);

        var releaseCmd = createdConn.CommandsCreated.Find(c => c.CommandText.Contains("sp_releaseapplock"));
        releaseCmd.Should().NotBeNull();
        releaseCmd!.Parameters["@LockOwner"].Value.Should().Be("Session");
    }

    [Fact]
    public async Task TryAcquireHandleAsync_SessionMode_WhenConnectionAlreadyOpen_DoesNotCallOpenAsync()
    {
        FakeDbConnection? createdConn = null;

        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.SetState(ConnectionState.Open);
                createdConn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(0);
                return createdConn;
            },
            _logger);

        var result = await provider.TryAcquireHandleAsync("res-already-open");

        result.IsSuccess.Should().BeTrue();
        createdConn!.OpenAsyncCallCount.Should().Be(0);

        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireHandleAsync_SessionMode_LockAlreadyHeld_ReturnsFailureAndDisposesConnection()
    {
        FakeDbConnection? createdConn = null;

        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(-1);
                return createdConn;
            },
            _logger);

        var result = await provider.TryAcquireHandleAsync("res-busy");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
        createdConn!.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_SessionMode_PreCanceledToken_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var factoryCalled = false;
        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                factoryCalled = true;
                return new FakeDbConnection();
            },
            _logger);

        var result = await provider.TryAcquireHandleAsync("res-cancel", cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
        factoryCalled.Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireHandleAsync_SessionMode_OperationCanceledException_ReturnsCanceledAndDisposes()
    {
        using var cts = new CancellationTokenSource();
        FakeDbConnection? createdConn = null;

        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.ExecuteScalarAsyncHandler = (_, token) =>
                {
                    cts.Cancel();
                    throw new OperationCanceledException(token);
                };
                return createdConn;
            },
            _logger);

        var result = await provider.TryAcquireHandleAsync("res-cancel-during", cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
        createdConn!.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_SessionMode_UnexpectedException_RethrowsAndDisposes()
    {
        FakeDbConnection? createdConn = null;

        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.ExecuteScalarAsyncHandler = (_, _) => throw new InvalidOperationException("Fatal SQL connection drop");
                return createdConn;
            },
            _logger);

        var act = () => provider.TryAcquireHandleAsync("res-fatal");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Fatal SQL connection drop*");

        createdConn!.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_SessionMode_Success_PassesTimeoutMs()
    {
        FakeDbCommand? executedCmd = null;

        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                var conn = new FakeDbConnection();
                conn.SetState(ConnectionState.Closed);
                conn.ExecuteScalarAsyncHandler = (cmd, _) =>
                {
                    executedCmd = cmd;
                    return Task.FromResult<object?>(0);
                };
                return conn;
            },
            _logger);

        var result = await provider.TryAcquireHandleAsync("res-timeout-test", TimeSpan.FromSeconds(3));

        result.IsSuccess.Should().BeTrue();
        executedCmd.Should().NotBeNull();
        executedCmd!.Parameters["@LockTimeout"].Value.Should().Be(3000);

        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_SessionMode_Failure_ReturnsTimeoutAndDisposes()
    {
        FakeDbConnection? createdConn = null;

        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(-1);
                return createdConn;
            },
            _logger);

        var result = await provider.TryAcquireHandleAsync("res-timeout-fail", TimeSpan.FromSeconds(2));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Timeout.Code);
        createdConn!.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithZeroTimeout_SessionMode_Success()
    {
        FakeDbCommand? executedCmd = null;
        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                var conn = new FakeDbConnection();
                conn.ExecuteScalarAsyncHandler = (cmd, _) =>
                {
                    executedCmd = cmd;
                    return Task.FromResult<object?>(0);
                };
                return conn;
            },
            _logger);

        var result = await provider.TryAcquireHandleAsync("res-zero-to", TimeSpan.Zero);
        result.IsSuccess.Should().BeTrue();
        executedCmd.Should().NotBeNull();
        executedCmd!.Parameters["@LockTimeout"].Value.Should().Be(0);
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_SessionMode_WhenAlreadyOpen_DoesNotCallOpenAsync()
    {
        FakeDbConnection? createdConn = null;
        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.SetState(ConnectionState.Open);
                createdConn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(0);
                return createdConn;
            },
            _logger);

        var result = await provider.TryAcquireHandleAsync("res-already-open-to", TimeSpan.FromSeconds(2));
        result.IsSuccess.Should().BeTrue();
        createdConn!.OpenAsyncCallCount.Should().Be(0);
        await result.Value.DisposeAsync();

        var releaseCmd = createdConn.CommandsCreated.Find(c => c.CommandText.Contains("sp_releaseapplock"));
        releaseCmd.Should().NotBeNull();
        releaseCmd!.Parameters["@LockOwner"].Value.Should().Be("Session");
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_NegativeTimeout_ThrowsArgumentOutOfRangeException()
    {
        var provider = new SqlServerDistributedLockProvider(() => new FakeDbConnection(), _logger);

        var act = () => provider.TryAcquireHandleAsync("res-neg", TimeSpan.FromSeconds(-1));
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithParameterName("timeout")
            .WithMessage("*Timeout must be non-negative or zero.*");
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_PreCanceledToken_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var factoryCalled = false;
        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                factoryCalled = true;
                return new FakeDbConnection();
            },
            _logger);

        var result = await provider.TryAcquireHandleAsync("res-pre-cancel-to", TimeSpan.FromSeconds(5), cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
        factoryCalled.Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_SessionMode_OperationCanceledException_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        FakeDbConnection? createdConn = null;

        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.ExecuteScalarAsyncHandler = (_, token) =>
                {
                    cts.Cancel();
                    throw new OperationCanceledException(token);
                };
                return createdConn;
            },
            _logger);

        var result = await provider.TryAcquireHandleAsync("res-cancel-to", TimeSpan.FromSeconds(5), cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
        createdConn!.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_SessionMode_UnexpectedException_RethrowsAndDisposes()
    {
        FakeDbConnection? createdConn = null;

        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.ExecuteScalarAsyncHandler = (_, _) => throw new InvalidOperationException("Timeout query explosion");
                return createdConn;
            },
            _logger);

        var act = () => provider.TryAcquireHandleAsync("res-fail-to", TimeSpan.FromSeconds(1));
        await act.Should().ThrowAsync<InvalidOperationException>();

        createdConn!.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task AcquireHandleAsync_SessionMode_Success_PassesMinusOneTimeout()
    {
        FakeDbCommand? executedCmd = null;
        FakeDbConnection? createdConn = null;

        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.SetState(ConnectionState.Closed);
                createdConn.ExecuteScalarAsyncHandler = (cmd, _) =>
                {
                    executedCmd = cmd;
                    return Task.FromResult<object?>((long)0);
                };
                return createdConn;
            },
            _logger);

        var result = await provider.AcquireHandleAsync("res-acquire-blocking");

        result.IsSuccess.Should().BeTrue();
        executedCmd.Should().NotBeNull();
        executedCmd!.Parameters["@LockTimeout"].Value.Should().Be(-1);

        await result.Value.DisposeAsync();

        var releaseCmd = createdConn!.CommandsCreated.Find(c => c.CommandText.Contains("sp_releaseapplock"));
        releaseCmd.Should().NotBeNull();
        releaseCmd!.Parameters["@LockOwner"].Value.Should().Be("Session");
    }

    [Fact]
    public async Task AcquireHandleAsync_SessionMode_WhenAlreadyOpen_DoesNotCallOpenAsync()
    {
        FakeDbConnection? createdConn = null;
        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.SetState(ConnectionState.Open);
                createdConn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(0);
                return createdConn;
            },
            _logger);

        var result = await provider.AcquireHandleAsync("res-already-open-acq");
        result.IsSuccess.Should().BeTrue();
        createdConn!.OpenAsyncCallCount.Should().Be(0);
        await result.Value.DisposeAsync();

        var releaseCmd = createdConn.CommandsCreated.Find(c => c.CommandText.Contains("sp_releaseapplock"));
        releaseCmd.Should().NotBeNull();
        releaseCmd!.Parameters["@LockOwner"].Value.Should().Be("Session");
    }

    [Fact]
    public async Task AcquireHandleAsync_SessionMode_Failure_ReturnsLockAlreadyHeldAndDisposes()
    {
        FakeDbConnection? createdConn = null;

        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(-999);
                return createdConn;
            },
            _logger);

        var result = await provider.AcquireHandleAsync("res-acquire-fail");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
        createdConn!.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task AcquireHandleAsync_SessionMode_PreCanceledToken_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        FakeDbConnection? createdConn = null;
        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.ExecuteScalarAsyncHandler = (_, _) =>
                {
                    cts.Cancel();
                    cts.Token.ThrowIfCancellationRequested();
                    return Task.FromResult<object?>(0);
                };
                return createdConn;
            },
            _logger);

        var result = await provider.AcquireHandleAsync("res-acquire-canceled", cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task AcquireHandleAsync_SessionMode_OperationCanceledException_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        FakeDbConnection? createdConn = null;

        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.ExecuteScalarAsyncHandler = (_, token) =>
                {
                    cts.Cancel();
                    throw new OperationCanceledException(token);
                };
                return createdConn;
            },
            _logger);

        var result = await provider.AcquireHandleAsync("res-acquire-canceled-op", cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
        createdConn!.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task AcquireHandleAsync_SessionMode_UnexpectedException_RethrowsAndDisposes()
    {
        FakeDbConnection? createdConn = null;

        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.ExecuteScalarAsyncHandler = (_, _) => throw new InvalidOperationException("Acquire failure");
                return createdConn;
            },
            _logger);

        var act = () => provider.AcquireHandleAsync("res-acquire-fatal");
        await act.Should().ThrowAsync<InvalidOperationException>();

        createdConn!.DisposeAsyncCallCount.Should().Be(1);
    }

    #endregion

    #region Transaction Lock (Ambient Connection)

    [Fact]
    public async Task TryAcquireHandleAsync_TransactionMode_DbConnection_Success()
    {
        var conn = new FakeDbConnection();
        FakeDbCommand? executedCmd = null;

        conn.ExecuteScalarAsyncHandler = (cmd, _) =>
        {
            executedCmd = cmd;
            return Task.FromResult<object?>(0);
        };

        var provider = new SqlServerDistributedLockProvider(conn, _logger);
        var result = await provider.TryAcquireHandleAsync("tx-order");

        result.IsSuccess.Should().BeTrue();
        result.Value.ResourceId.Should().Be("tx-order");
        result.Value.LockId.Should().NotBe(0);
        result.Value.HandleLostToken.Should().Be(CancellationToken.None);
        await result.Value.DisposeAsync();

        executedCmd.Should().NotBeNull();
        executedCmd!.Parameters["@LockOwner"].Value.Should().Be("Transaction");
        executedCmd.Parameters["@LockTimeout"].Value.Should().Be(0);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_TransactionMode_DbConnection_Failure_ReturnsLockAlreadyHeld()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(-1);

        var provider = new SqlServerDistributedLockProvider(conn, _logger);
        var result = await provider.TryAcquireHandleAsync("tx-busy");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_TransactionMode_NonDbConnection_Success()
    {
        var syncConn = new FakeSyncConnection();
        FakeSyncCommand? executedCmd = null;

        syncConn.ExecuteScalarHandler = cmd =>
        {
            executedCmd = cmd;
            return 0;
        };

        var provider = new SqlServerDistributedLockProvider(syncConn, _logger);
        var result = await provider.TryAcquireHandleAsync("sync-tx-order");

        result.IsSuccess.Should().BeTrue();
        executedCmd.Should().NotBeNull();
        executedCmd!.CommandText.Should().Contain("sys.sp_getapplock");
        ((IDbDataParameter)executedCmd.Parameters["@Resource"]).ParameterName.Should().Be("@Resource");
        ((IDbDataParameter)executedCmd.Parameters["@Resource"]).Value.Should().Be("sync-tx-order");
        ((IDbDataParameter)executedCmd.Parameters["@LockOwner"]).Value.Should().Be("Transaction");
        ((IDbDataParameter)executedCmd.Parameters["@LockTimeout"]).ParameterName.Should().Be("@LockTimeout");
        ((IDbDataParameter)executedCmd.Parameters["@LockTimeout"]).Value.Should().Be(0);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_TransactionMode_NonDbConnection_Failure_ReturnsLockAlreadyHeld()
    {
        var syncConn = new FakeSyncConnection();
        syncConn.ExecuteScalarHandler = _ => -1;

        var provider = new SqlServerDistributedLockProvider(syncConn, _logger);
        var result = await provider.TryAcquireHandleAsync("sync-tx-busy");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_TransactionMode_PreCanceled_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var conn = new FakeDbConnection();
        var provider = new SqlServerDistributedLockProvider(conn, _logger);

        var result = await provider.TryAcquireHandleAsync("tx-cancel", cts.Token);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_TransactionMode_OperationCanceledException_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        var conn = new FakeDbConnection();
        conn.ExecuteScalarAsyncHandler = (_, token) =>
        {
            cts.Cancel();
            throw new OperationCanceledException(token);
        };

        var provider = new SqlServerDistributedLockProvider(conn, _logger);
        var result = await provider.TryAcquireHandleAsync("tx-cancel-op", cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_TransactionMode_DbConnection_Success()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(0);

        var provider = new SqlServerDistributedLockProvider(conn, _logger);
        var result = await provider.TryAcquireHandleAsync("tx-to-res", TimeSpan.FromSeconds(4));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_TransactionMode_DbConnection_Failure_ReturnsTimeout()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(-1);

        var provider = new SqlServerDistributedLockProvider(conn, _logger);
        var result = await provider.TryAcquireHandleAsync("tx-to-fail", TimeSpan.FromSeconds(4));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Timeout.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_TransactionMode_NonDbConnection_Success()
    {
        var syncConn = new FakeSyncConnection();
        syncConn.ExecuteScalarHandler = _ => 0;

        var provider = new SqlServerDistributedLockProvider(syncConn, _logger);
        var result = await provider.TryAcquireHandleAsync("sync-to-res", TimeSpan.FromSeconds(2));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_TransactionMode_NonDbConnection_Failure_ReturnsTimeout()
    {
        var syncConn = new FakeSyncConnection();
        syncConn.ExecuteScalarHandler = _ => -1;

        var provider = new SqlServerDistributedLockProvider(syncConn, _logger);
        var result = await provider.TryAcquireHandleAsync("sync-to-fail", TimeSpan.FromSeconds(2));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Timeout.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_TransactionMode_OperationCanceledException_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        var conn = new FakeDbConnection();
        conn.ExecuteScalarAsyncHandler = (_, token) =>
        {
            cts.Cancel();
            throw new OperationCanceledException(token);
        };

        var provider = new SqlServerDistributedLockProvider(conn, _logger);
        var result = await provider.TryAcquireHandleAsync("tx-to-cancel", TimeSpan.FromSeconds(3), cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task AcquireHandleAsync_TransactionMode_DbConnection_Success()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(0);

        var provider = new SqlServerDistributedLockProvider(conn, _logger);
        var result = await provider.AcquireHandleAsync("tx-acq-res");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AcquireHandleAsync_TransactionMode_DbConnection_Failure_ReturnsLockAlreadyHeld()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(-1);

        var provider = new SqlServerDistributedLockProvider(conn, _logger);
        var result = await provider.AcquireHandleAsync("tx-acq-fail");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task AcquireHandleAsync_TransactionMode_NonDbConnection_Success()
    {
        var syncConn = new FakeSyncConnection();
        syncConn.ExecuteScalarHandler = _ => 0;

        var provider = new SqlServerDistributedLockProvider(syncConn, _logger);
        var result = await provider.AcquireHandleAsync("sync-acq-res");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AcquireHandleAsync_TransactionMode_NonDbConnection_Failure_ReturnsLockAlreadyHeld()
    {
        var syncConn = new FakeSyncConnection();
        syncConn.ExecuteScalarHandler = _ => -1;

        var provider = new SqlServerDistributedLockProvider(syncConn, _logger);
        var result = await provider.AcquireHandleAsync("sync-acq-fail");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task AcquireHandleAsync_TransactionMode_OperationCanceledException_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        var conn = new FakeDbConnection();
        conn.ExecuteScalarAsyncHandler = (_, token) =>
        {
            cts.Cancel();
            throw new OperationCanceledException(token);
        };

        var provider = new SqlServerDistributedLockProvider(conn, _logger);
        var result = await provider.AcquireHandleAsync("tx-acq-cancel", cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    #endregion

    #region IAsyncDisposable Overloads

    [Fact]
    public async Task TryAcquireAsync_Success_ReturnsAsyncDisposable()
    {
        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                var conn = new FakeDbConnection();
                conn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(0);
                return conn;
            },
            _logger);

        var result = await provider.TryAcquireAsync("res-disp");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_Failure_ReturnsFailureResult()
    {
        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                var conn = new FakeDbConnection();
                conn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(-1);
                return conn;
            },
            _logger);

        var result = await provider.TryAcquireAsync("res-disp-fail");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task TryAcquireAsync_WithTimeout_Success_ReturnsAsyncDisposable()
    {
        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                var conn = new FakeDbConnection();
                conn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(0);
                return conn;
            },
            _logger);

        var result = await provider.TryAcquireAsync("res-disp-to", TimeSpan.FromSeconds(2));

        result.IsSuccess.Should().BeTrue();
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_WithTimeout_Failure_ReturnsFailureResult()
    {
        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                var conn = new FakeDbConnection();
                conn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(-1);
                return conn;
            },
            _logger);

        var result = await provider.TryAcquireAsync("res-disp-to-fail", TimeSpan.FromSeconds(2));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Timeout.Code);
    }

    [Fact]
    public async Task AcquireAsync_Success_ReturnsAsyncDisposable()
    {
        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                var conn = new FakeDbConnection();
                conn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(0);
                return conn;
            },
            _logger);

        var result = await provider.AcquireAsync("res-disp-acq");

        result.IsSuccess.Should().BeTrue();
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_Failure_ReturnsFailureResult()
    {
        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                var conn = new FakeDbConnection();
                conn.ExecuteScalarAsyncHandler = (_, _) => Task.FromResult<object?>(-1);
                return conn;
            },
            _logger);

        var result = await provider.AcquireAsync("res-disp-acq-fail");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    #endregion

    #region Key Normalization & Guards

    [Fact]
    public async Task TryAcquireHandleAsync_LongResourceId_HashesKeyWithSha256()
    {
        var longKey = new string('z', 300);
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(longKey)));

        FakeDbCommand? executedCmd = null;

        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                var conn = new FakeDbConnection();
                conn.ExecuteScalarAsyncHandler = (cmd, _) =>
                {
                    executedCmd = cmd;
                    return Task.FromResult<object?>(0);
                };
                return conn;
            },
            _logger);

        var result = await provider.TryAcquireHandleAsync(longKey);

        result.IsSuccess.Should().BeTrue();
        executedCmd.Should().NotBeNull();
        executedCmd!.Parameters["@Resource"].Value.Should().Be(expectedHash);

        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithExactly255Characters_PreservesKeyIntact()
    {
        var exactKey = new string('m', 255);
        FakeDbCommand? executedCmd = null;

        var provider = new SqlServerDistributedLockProvider(
            () =>
            {
                var conn = new FakeDbConnection();
                conn.ExecuteScalarAsyncHandler = (cmd, _) =>
                {
                    executedCmd = cmd;
                    return Task.FromResult<object?>(0);
                };
                return conn;
            },
            _logger);

        var result = await provider.TryAcquireHandleAsync(exactKey);
        result.IsSuccess.Should().BeTrue();
        executedCmd.Should().NotBeNull();
        executedCmd!.Parameters["@Resource"].Value.Should().Be(exactKey);
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithExplicitOptions_PropagatesCommandTimeout()
    {
        var conn = new FakeDbConnection();
        FakeDbCommand? executedCmd = null;
        conn.ExecuteScalarAsyncHandler = (cmd, _) =>
        {
            executedCmd = cmd;
            return Task.FromResult<object?>(0);
        };

        var provider = new SqlServerDistributedLockProvider(conn, _logger, new SqlServerLockOptions { CommandTimeoutSeconds = 88 });
        var result = await provider.TryAcquireHandleAsync("tx-options-res");
        result.IsSuccess.Should().BeTrue();
        executedCmd.Should().NotBeNull();
        executedCmd!.CommandTimeout.Should().Be(88);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task TryAcquireHandleAsync_InvalidResourceId_ThrowsArgumentException(string? resourceId)
    {
        var provider = new SqlServerDistributedLockProvider(() => new FakeDbConnection(), _logger);
        var act = () => provider.TryAcquireHandleAsync(resourceId!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task TryAcquireHandleAsync_WithTimeout_InvalidResourceId_ThrowsArgumentException(string? resourceId)
    {
        var provider = new SqlServerDistributedLockProvider(() => new FakeDbConnection(), _logger);
        var act = () => provider.TryAcquireHandleAsync(resourceId!, TimeSpan.FromSeconds(5));
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task AcquireHandleAsync_InvalidResourceId_ThrowsArgumentException(string? resourceId)
    {
        var provider = new SqlServerDistributedLockProvider(() => new FakeDbConnection(), _logger);
        var act = () => provider.AcquireHandleAsync(resourceId!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task NormalizeResourceKey_LongKeyAndMatchingHex_DoNotProduceKeyCollision()
    {
        string longKey = new string('x', 300);
        FakeDbCommand? longKeyCmd = null;

        var conn1 = new FakeDbConnection();
        conn1.ExecuteScalarAsyncHandler = (cmd, _) =>
        {
            longKeyCmd = cmd;
            return Task.FromResult<object?>(0);
        };

        var provider1 = new SqlServerDistributedLockProvider(() => conn1, _logger);
        var res1 = await provider1.TryAcquireHandleAsync(longKey);
        res1.IsSuccess.Should().BeTrue();

        string normalizedLongKey = (string)longKeyCmd!.Parameters["@Resource"].Value!;
        normalizedLongKey.Length.Should().Be(64);

        FakeDbCommand? matchingKeyCmd = null;
        var conn2 = new FakeDbConnection();
        conn2.ExecuteScalarAsyncHandler = (cmd, _) =>
        {
            matchingKeyCmd = cmd;
            return Task.FromResult<object?>(0);
        };

        var provider2 = new SqlServerDistributedLockProvider(() => conn2, _logger);
        var res2 = await provider2.TryAcquireHandleAsync(normalizedLongKey);
        res2.IsSuccess.Should().BeTrue();

        string normalizedMatchingKey = (string)matchingKeyCmd!.Parameters["@Resource"].Value!;

        normalizedLongKey.Should().NotBe(normalizedMatchingKey);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_LargeTimeout_DoesNotThrowOverflowException()
    {
        FakeDbCommand? executedCmd = null;
        var conn = new FakeDbConnection();
        conn.ExecuteScalarAsyncHandler = (cmd, _) =>
        {
            executedCmd = cmd;
            return Task.FromResult<object?>(0);
        };

        var provider = new SqlServerDistributedLockProvider(() => conn, _logger);
        var result = await provider.TryAcquireHandleAsync("res-large-timeout", TimeSpan.FromDays(40));

        result.IsSuccess.Should().BeTrue();
        executedCmd.Should().NotBeNull();
        executedCmd!.Parameters["@LockTimeout"].Value.Should().Be(int.MaxValue);
    }

    #endregion
}
