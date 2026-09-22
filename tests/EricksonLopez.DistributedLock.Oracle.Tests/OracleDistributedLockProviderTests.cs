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
using Microsoft.Extensions.Options;
using Xunit;

namespace EricksonLopez.DistributedLock.Oracle.Tests;

public sealed class OracleDistributedLockProviderTests
{
    private readonly NullLogger<OracleDistributedLockProvider> _logger = NullLogger<OracleDistributedLockProvider>.Instance;

    [Fact]
    public void Constructor_WithConnection_NullConnection_ThrowsArgumentNullException()
    {
        var act = () => new OracleDistributedLockProvider((IDbConnection)null!, _logger);
        act.Should().Throw<ArgumentNullException>().WithParameterName("connection");
    }

    [Fact]
    public void Constructor_WithConnection_NullLogger_ThrowsArgumentNullException()
    {
        var conn = new FakeDbConnection();
        var act = () => new OracleDistributedLockProvider(conn, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithFactory_NullFactory_ThrowsArgumentNullException()
    {
        var act = () => new OracleDistributedLockProvider((Func<DbConnection>)null!, _logger);
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_WithFactory_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new OracleDistributedLockProvider(() => new FakeDbConnection(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithOptionsWrapper_InitializesCorrectly()
    {
        var options = Options.Create(new OracleLockOptions { CommandTimeoutSeconds = 40 });
        var provider = new OracleDistributedLockProvider(() => new FakeDbConnection(), _logger, options);
        provider.Should().NotBeNull();
    }

    #region Session Lock (Connection Factory)

    [Fact]
    public async Task TryAcquireHandleAsync_SessionMode_Success_OpensClosedConnectionAndReturnsHandle()
    {
        FakeDbConnection? createdConn = null;
        FakeDbCommand? executedCmd = null;

        var provider = new OracleDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.SetState(ConnectionState.Closed);
                createdConn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
                {
                    executedCmd = cmd;
                    cmd.Parameters["OutStatus"].Value = 0;
                    cmd.Parameters["OutHandle"].Value = "ORA_SESSION_HANDLE_1";
                    return Task.FromResult(1);
                };
                return createdConn;
            },
            _logger,
            new OracleLockOptions { CommandTimeoutSeconds = 20 });

        var result = await provider.TryAcquireHandleAsync("order-invoice");

        result.IsSuccess.Should().BeTrue();
        result.Value.ResourceId.Should().Be("order-invoice");

        createdConn.Should().NotBeNull();
        createdConn!.OpenAsyncCallCount.Should().Be(1);

        executedCmd.Should().NotBeNull();
        executedCmd!.CommandText.Should().Contain("DBMS_LOCK.ALLOCATE_UNIQUE");
        executedCmd.CommandText.Should().Contain("DBMS_LOCK.REQUEST(v_handle, 6, :Timeout, FALSE);");
        executedCmd.Parameters["LockName"].Value.Should().Be("order-invoice");
        executedCmd.Parameters["Timeout"].Value.Should().Be(0);
        executedCmd.CommandTimeout.Should().Be(20);

        await result.Value.DisposeAsync();
        createdConn.DisposeAsyncCallCount.Should().Be(1);

        var releaseCmd = createdConn.CommandsCreated.Find(c => c.CommandText.Contains("DBMS_LOCK.RELEASE"));
        releaseCmd.Should().NotBeNull();
        releaseCmd!.Parameters["LockHandle"].Value.Should().Be("ORA_SESSION_HANDLE_1");
    }

    [Fact]
    public async Task TryAcquireHandleAsync_SessionMode_WhenConnectionAlreadyOpen_DoesNotCallOpenAsync()
    {
        FakeDbConnection? createdConn = null;

        var provider = new OracleDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.SetState(ConnectionState.Open);
                createdConn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
                {
                    cmd.Parameters["OutStatus"].Value = 0;
                    cmd.Parameters["OutHandle"].Value = "HANDLE_OPEN";
                    return Task.FromResult(1);
                };
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

        var provider = new OracleDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
                {
                    cmd.Parameters["OutStatus"].Value = 1; // Timeout / Held
                    return Task.FromResult(1);
                };
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
        var provider = new OracleDistributedLockProvider(
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

        var provider = new OracleDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.ExecuteNonQueryAsyncHandler = (_, token) =>
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

        var provider = new OracleDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.ExecuteNonQueryAsyncHandler = (_, _) => throw new InvalidOperationException("Fatal Oracle ORA-03113 drop");
                return createdConn;
            },
            _logger);

        var act = () => provider.TryAcquireHandleAsync("res-fatal");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Fatal Oracle ORA-03113 drop*");

        createdConn!.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_SessionMode_Success_PassesCeilingSeconds()
    {
        FakeDbCommand? executedCmd = null;

        var provider = new OracleDistributedLockProvider(
            () =>
            {
                var conn = new FakeDbConnection();
                conn.SetState(ConnectionState.Closed);
                conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
                {
                    executedCmd = cmd;
                    cmd.Parameters["OutStatus"].Value = 0;
                    cmd.Parameters["OutHandle"].Value = "ORA_HANDLE_TIMEOUT";
                    return Task.FromResult(1);
                };
                return conn;
            },
            _logger);

        var result = await provider.TryAcquireHandleAsync("res-timeout-test", TimeSpan.FromMilliseconds(2500));

        result.IsSuccess.Should().BeTrue();
        executedCmd.Should().NotBeNull();
        executedCmd!.Parameters["Timeout"].Value.Should().Be(3);

        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireHandleAsync_ZeroTimeout_SucceedsWithoutThrowing()
    {
        var conn = new FakeDbConnection();
        FakeDbCommand? executedCmd = null;
        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            executedCmd = cmd;
            cmd.Parameters["OutStatus"].Value = 0;
            cmd.Parameters["OutHandle"].Value = "ORA_ZERO_TIMEOUT";
            return Task.FromResult(1);
        };

        var provider = new OracleDistributedLockProvider(() => conn, _logger);
        var result = await provider.TryAcquireHandleAsync("res-zero-timeout", TimeSpan.Zero);

        result.IsSuccess.Should().BeTrue();
        executedCmd.Should().NotBeNull();
        executedCmd!.Parameters["Timeout"].Value.Should().Be(0);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_SessionMode_WhenAlreadyOpen_DoesNotCallOpenAsync()
    {
        FakeDbConnection? createdConn = null;

        var provider = new OracleDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.SetState(ConnectionState.Open);
                createdConn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
                {
                    cmd.Parameters["OutStatus"].Value = 0;
                    return Task.FromResult(1);
                };
                return createdConn;
            },
            _logger);

        var result = await provider.TryAcquireHandleAsync("res-already-open-to", TimeSpan.FromSeconds(2));

        result.IsSuccess.Should().BeTrue();
        createdConn!.OpenAsyncCallCount.Should().Be(0);

        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_SessionMode_StatusOne_ReturnsTimeoutAndDisposes()
    {
        FakeDbConnection? createdConn = null;

        var provider = new OracleDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
                {
                    cmd.Parameters["OutStatus"].Value = 1; // Timeout
                    return Task.FromResult(1);
                };
                return createdConn;
            },
            _logger);

        var result = await provider.TryAcquireHandleAsync("res-timeout-fail", TimeSpan.FromSeconds(2));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Timeout.Code);
        createdConn!.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_SessionMode_StatusTwo_ReturnsTimeoutAndDisposes()
    {
        FakeDbConnection? createdConn = null;

        var provider = new OracleDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
                {
                    cmd.Parameters["OutStatus"].Value = 2; // Deadlock
                    return Task.FromResult(1);
                };
                return createdConn;
            },
            _logger);

        var result = await provider.TryAcquireHandleAsync("res-deadlock-fail", TimeSpan.FromSeconds(2));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Timeout.Code);
        createdConn!.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_NegativeTimeout_ThrowsArgumentOutOfRangeException()
    {
        var provider = new OracleDistributedLockProvider(() => new FakeDbConnection(), _logger);

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
        var provider = new OracleDistributedLockProvider(
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

        var provider = new OracleDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.ExecuteNonQueryAsyncHandler = (_, token) =>
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

        var provider = new OracleDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.ExecuteNonQueryAsyncHandler = (_, _) => throw new InvalidOperationException("Timeout query explosion");
                return createdConn;
            },
            _logger);

        var act = () => provider.TryAcquireHandleAsync("res-fail-to", TimeSpan.FromSeconds(1));
        await act.Should().ThrowAsync<InvalidOperationException>();

        createdConn!.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task AcquireHandleAsync_SessionMode_Success_PassesMaxWaitSeconds()
    {
        FakeDbCommand? executedCmd = null;
        FakeDbConnection? createdConn = null;

        var provider = new OracleDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.SetState(ConnectionState.Closed);
                createdConn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
                {
                    executedCmd = cmd;
                    cmd.Parameters["OutStatus"].Value = 0;
                    cmd.Parameters["OutHandle"].Value = "ORA_BLOCKING_HANDLE";
                    return Task.FromResult(1);
                };
                return createdConn;
            },
            _logger);

        var result = await provider.AcquireHandleAsync("res-acquire-blocking");

        result.IsSuccess.Should().BeTrue();
        executedCmd.Should().NotBeNull();
        executedCmd!.Parameters["Timeout"].Value.Should().Be(32767);

        await result.Value.DisposeAsync();

        var releaseCmd = createdConn!.CommandsCreated.Find(c => c.CommandText.Contains("DBMS_LOCK.RELEASE"));
        releaseCmd.Should().NotBeNull();
    }

    [Fact]
    public async Task AcquireHandleAsync_SessionMode_WhenAlreadyOpen_DoesNotCallOpenAsync()
    {
        FakeDbConnection? createdConn = null;

        var provider = new OracleDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.SetState(ConnectionState.Open);
                createdConn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
                {
                    cmd.Parameters["OutStatus"].Value = 0;
                    return Task.FromResult(1);
                };
                return createdConn;
            },
            _logger);

        var result = await provider.AcquireHandleAsync("res-already-open-acq");

        result.IsSuccess.Should().BeTrue();
        createdConn!.OpenAsyncCallCount.Should().Be(0);

        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task AcquireHandleAsync_SessionMode_Failure_ReturnsLockAlreadyHeldAndDisposes()
    {
        FakeDbConnection? createdConn = null;

        var provider = new OracleDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
                {
                    cmd.Parameters["OutStatus"].Value = 1;
                    return Task.FromResult(1);
                };
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
        var provider = new OracleDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
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

        var provider = new OracleDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.ExecuteNonQueryAsyncHandler = (_, token) =>
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

        var provider = new OracleDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.ExecuteNonQueryAsyncHandler = (_, _) => throw new InvalidOperationException("Acquire failure");
                return createdConn;
            },
            _logger);

        var act = () => provider.AcquireHandleAsync("res-acquire-fatal");
        await act.Should().ThrowAsync<InvalidOperationException>();

        createdConn!.DisposeAsyncCallCount.Should().Be(1);
    }

    #endregion

    #region Ambient Connection Locks

    [Fact]
    public async Task TryAcquireHandleAsync_AmbientMode_DbConnection_Success()
    {
        var conn = new FakeDbConnection();
        FakeDbCommand? executedCmd = null;

        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            executedCmd = cmd;
            cmd.Parameters["OutStatus"].Value = 0;
            cmd.Parameters["OutHandle"].Value = "AMB_HANDLE";
            return Task.FromResult(1);
        };

        var provider = new OracleDistributedLockProvider(conn, _logger);
        var result = await provider.TryAcquireHandleAsync("amb-order");

        result.IsSuccess.Should().BeTrue();
        result.Value.ResourceId.Should().Be("amb-order");
        result.Value.LockId.Should().NotBe(0);
        result.Value.HandleLostToken.Should().Be(CancellationToken.None);
        await result.Value.DisposeAsync();

        executedCmd.Should().NotBeNull();
        executedCmd!.Parameters["Timeout"].Value.Should().Be(0);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_AmbientMode_DbConnection_Failure_ReturnsLockAlreadyHeld()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            cmd.Parameters["OutStatus"].Value = 1;
            return Task.FromResult(1);
        };

        var provider = new OracleDistributedLockProvider(conn, _logger);
        var result = await provider.TryAcquireHandleAsync("amb-busy");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_AmbientMode_NonDbConnection_Success()
    {
        var syncConn = new FakeSyncConnection();
        FakeSyncCommand? executedCmd = null;

        syncConn.ExecuteNonQueryHandler = cmd =>
        {
            executedCmd = cmd;
            ((IDbDataParameter)cmd.Parameters["OutStatus"]).Value = 0;
            ((IDbDataParameter)cmd.Parameters["OutHandle"]).Value = null;
            return 1;
        };

        var provider = new OracleDistributedLockProvider(syncConn, _logger);
        var result = await provider.TryAcquireHandleAsync("sync-amb-order");

        result.IsSuccess.Should().BeTrue();
        executedCmd.Should().NotBeNull();
        executedCmd!.CommandText.Should().Contain("DBMS_LOCK.ALLOCATE_UNIQUE");
        executedCmd.CommandText.Should().Contain("DBMS_LOCK.REQUEST(v_handle, 6, :Timeout, TRUE);");
        ((IDbDataParameter)executedCmd.Parameters["LockName"]).ParameterName.Should().Be("LockName");
        ((IDbDataParameter)executedCmd.Parameters["LockName"]).Value.Should().Be("sync-amb-order");
        ((IDbDataParameter)executedCmd.Parameters["Timeout"]).ParameterName.Should().Be("Timeout");
        ((IDbDataParameter)executedCmd.Parameters["Timeout"]).Value.Should().Be(0);
        ((IDbDataParameter)executedCmd.Parameters["OutHandle"]).ParameterName.Should().Be("OutHandle");
        ((IDbDataParameter)executedCmd.Parameters["OutStatus"]).ParameterName.Should().Be("OutStatus");
    }

    [Fact]
    public async Task TryAcquireHandleAsync_AmbientMode_NonDbConnection_Failure_ReturnsLockAlreadyHeld()
    {
        var syncConn = new FakeSyncConnection();
        syncConn.ExecuteNonQueryHandler = cmd =>
        {
            ((IDbDataParameter)cmd.Parameters["OutStatus"]).Value = 1;
            return 1;
        };

        var provider = new OracleDistributedLockProvider(syncConn, _logger);
        var result = await provider.TryAcquireHandleAsync("sync-amb-busy");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_AmbientMode_PreCanceled_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var conn = new FakeDbConnection();
        var provider = new OracleDistributedLockProvider(conn, _logger);

        var result = await provider.TryAcquireHandleAsync("amb-cancel", cts.Token);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_AmbientMode_OperationCanceledException_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        var conn = new FakeDbConnection();
        conn.ExecuteNonQueryAsyncHandler = (_, token) =>
        {
            cts.Cancel();
            throw new OperationCanceledException(token);
        };

        var provider = new OracleDistributedLockProvider(conn, _logger);
        var result = await provider.TryAcquireHandleAsync("amb-cancel-op", cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_AmbientMode_DbConnection_Success()
    {
        var conn = new FakeDbConnection();
        FakeDbCommand? executedCmd = null;
        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            executedCmd = cmd;
            cmd.Parameters["OutStatus"].Value = 0;
            return Task.FromResult(1);
        };

        var provider = new OracleDistributedLockProvider(conn, _logger);
        var result = await provider.TryAcquireHandleAsync("amb-to-res", TimeSpan.FromSeconds(4));

        result.IsSuccess.Should().BeTrue();
        executedCmd.Should().NotBeNull();
        executedCmd!.CommandText.Should().Contain("DBMS_LOCK.REQUEST(v_handle, 6, :Timeout, TRUE);");
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_AmbientMode_DbConnection_StatusOne_ReturnsTimeout()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            cmd.Parameters["OutStatus"].Value = 1;
            return Task.FromResult(1);
        };

        var provider = new OracleDistributedLockProvider(conn, _logger);
        var result = await provider.TryAcquireHandleAsync("amb-to-fail", TimeSpan.FromSeconds(4));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Timeout.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_AmbientMode_DbConnection_StatusTwo_ReturnsTimeout()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            cmd.Parameters["OutStatus"].Value = 2;
            return Task.FromResult(1);
        };

        var provider = new OracleDistributedLockProvider(conn, _logger);
        var result = await provider.TryAcquireHandleAsync("amb-to-busy", TimeSpan.FromSeconds(4));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Timeout.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_AmbientMode_NonDbConnection_Success()
    {
        var syncConn = new FakeSyncConnection();
        syncConn.ExecuteNonQueryHandler = cmd =>
        {
            ((IDbDataParameter)cmd.Parameters["OutStatus"]).Value = 0;
            return 1;
        };

        var provider = new OracleDistributedLockProvider(syncConn, _logger);
        var result = await provider.TryAcquireHandleAsync("sync-to-res", TimeSpan.FromSeconds(2));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_AmbientMode_NonDbConnection_StatusOne_ReturnsTimeout()
    {
        var syncConn = new FakeSyncConnection();
        syncConn.ExecuteNonQueryHandler = cmd =>
        {
            ((IDbDataParameter)cmd.Parameters["OutStatus"]).Value = 1;
            return 1;
        };

        var provider = new OracleDistributedLockProvider(syncConn, _logger);
        var result = await provider.TryAcquireHandleAsync("sync-to-fail", TimeSpan.FromSeconds(2));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Timeout.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_AmbientMode_NonDbConnection_StatusTwo_ReturnsTimeout()
    {
        var syncConn = new FakeSyncConnection();
        syncConn.ExecuteNonQueryHandler = cmd =>
        {
            ((IDbDataParameter)cmd.Parameters["OutStatus"]).Value = 2;
            return 1;
        };

        var provider = new OracleDistributedLockProvider(syncConn, _logger);
        var result = await provider.TryAcquireHandleAsync("sync-to-busy", TimeSpan.FromSeconds(2));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Timeout.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_AmbientMode_OperationCanceledException_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        var conn = new FakeDbConnection();
        conn.ExecuteNonQueryAsyncHandler = (_, token) =>
        {
            cts.Cancel();
            throw new OperationCanceledException(token);
        };

        var provider = new OracleDistributedLockProvider(conn, _logger);
        var result = await provider.TryAcquireHandleAsync("amb-to-cancel", TimeSpan.FromSeconds(3), cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task AcquireHandleAsync_AmbientMode_DbConnection_Success()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            cmd.Parameters["OutStatus"].Value = 0;
            return Task.FromResult(1);
        };

        var provider = new OracleDistributedLockProvider(conn, _logger);
        var result = await provider.AcquireHandleAsync("amb-acq-res");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AcquireHandleAsync_AmbientMode_DbConnection_Failure_ReturnsLockAlreadyHeld()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            cmd.Parameters["OutStatus"].Value = 1;
            return Task.FromResult(1);
        };

        var provider = new OracleDistributedLockProvider(conn, _logger);
        var result = await provider.AcquireHandleAsync("amb-acq-fail");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task AcquireHandleAsync_AmbientMode_NonDbConnection_Success()
    {
        var syncConn = new FakeSyncConnection();
        syncConn.ExecuteNonQueryHandler = cmd =>
        {
            ((IDbDataParameter)cmd.Parameters["OutStatus"]).Value = 0;
            return 1;
        };

        var provider = new OracleDistributedLockProvider(syncConn, _logger);
        var result = await provider.AcquireHandleAsync("sync-acq-res");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AcquireHandleAsync_AmbientMode_NonDbConnection_Failure_ReturnsLockAlreadyHeld()
    {
        var syncConn = new FakeSyncConnection();
        syncConn.ExecuteNonQueryHandler = cmd =>
        {
            ((IDbDataParameter)cmd.Parameters["OutStatus"]).Value = 1;
            return 1;
        };

        var provider = new OracleDistributedLockProvider(syncConn, _logger);
        var result = await provider.AcquireHandleAsync("sync-acq-fail");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task AcquireHandleAsync_AmbientMode_OperationCanceledException_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        var conn = new FakeDbConnection();
        conn.ExecuteNonQueryAsyncHandler = (_, token) =>
        {
            cts.Cancel();
            throw new OperationCanceledException(token);
        };

        var provider = new OracleDistributedLockProvider(conn, _logger);
        var result = await provider.AcquireHandleAsync("amb-acq-cancel", cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    #endregion

    #region IAsyncDisposable Overloads

    [Fact]
    public async Task TryAcquireAsync_Success_ReturnsAsyncDisposable()
    {
        var provider = new OracleDistributedLockProvider(
            () =>
            {
                var conn = new FakeDbConnection();
                conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
                {
                    cmd.Parameters["OutStatus"].Value = 0;
                    return Task.FromResult(1);
                };
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
        var provider = new OracleDistributedLockProvider(
            () =>
            {
                var conn = new FakeDbConnection();
                conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
                {
                    cmd.Parameters["OutStatus"].Value = 1;
                    return Task.FromResult(1);
                };
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
        var provider = new OracleDistributedLockProvider(
            () =>
            {
                var conn = new FakeDbConnection();
                conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
                {
                    cmd.Parameters["OutStatus"].Value = 0;
                    return Task.FromResult(1);
                };
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
        var provider = new OracleDistributedLockProvider(
            () =>
            {
                var conn = new FakeDbConnection();
                conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
                {
                    cmd.Parameters["OutStatus"].Value = 1;
                    return Task.FromResult(1);
                };
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
        var provider = new OracleDistributedLockProvider(
            () =>
            {
                var conn = new FakeDbConnection();
                conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
                {
                    cmd.Parameters["OutStatus"].Value = 0;
                    return Task.FromResult(1);
                };
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
        var provider = new OracleDistributedLockProvider(
            () =>
            {
                var conn = new FakeDbConnection();
                conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
                {
                    cmd.Parameters["OutStatus"].Value = 1;
                    return Task.FromResult(1);
                };
                return conn;
            },
            _logger);

        var result = await provider.AcquireAsync("res-disp-acq-fail");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    #endregion

    #region Key Normalization, Options & Guards

    [Fact]
    public async Task TryAcquireHandleAsync_LongResourceId_HashesKeyWithSha256()
    {
        var longKey = new string('z', 200);
        var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(longKey)));

        FakeDbCommand? executedCmd = null;

        var provider = new OracleDistributedLockProvider(
            () =>
            {
                var conn = new FakeDbConnection();
                conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
                {
                    executedCmd = cmd;
                    cmd.Parameters["OutStatus"].Value = 0;
                    return Task.FromResult(1);
                };
                return conn;
            },
            _logger);

        var result = await provider.TryAcquireHandleAsync(longKey);

        result.IsSuccess.Should().BeTrue();
        executedCmd.Should().NotBeNull();
        executedCmd!.Parameters["LockName"].Value.Should().Be(expectedHash);

        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithExactly128Characters_PreservesKeyIntact()
    {
        var exactKey = new string('o', 128);
        FakeDbCommand? executedCmd = null;

        var provider = new OracleDistributedLockProvider(
            () =>
            {
                var conn = new FakeDbConnection();
                conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
                {
                    executedCmd = cmd;
                    cmd.Parameters["OutStatus"].Value = 0;
                    return Task.FromResult(1);
                };
                return conn;
            },
            _logger);

        var result = await provider.TryAcquireHandleAsync(exactKey);
        result.IsSuccess.Should().BeTrue();
        executedCmd.Should().NotBeNull();
        executedCmd!.Parameters["LockName"].Value.Should().Be(exactKey);
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithExplicitOptions_PropagatesCommandTimeout()
    {
        var conn = new FakeDbConnection();
        FakeDbCommand? executedCmd = null;
        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            executedCmd = cmd;
            cmd.Parameters["OutStatus"].Value = 0;
            return Task.FromResult(1);
        };

        var provider = new OracleDistributedLockProvider(conn, _logger, new OracleLockOptions { CommandTimeoutSeconds = 77 });
        var result = await provider.TryAcquireHandleAsync("tx-options-res");
        result.IsSuccess.Should().BeTrue();
        executedCmd.Should().NotBeNull();
        executedCmd!.CommandTimeout.Should().Be(77);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task TryAcquireHandleAsync_InvalidResourceId_ThrowsArgumentException(string? resourceId)
    {
        var provider = new OracleDistributedLockProvider(() => new FakeDbConnection(), _logger);
        var act = () => provider.TryAcquireHandleAsync(resourceId!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task TryAcquireHandleAsync_WithTimeout_InvalidResourceId_ThrowsArgumentException(string? resourceId)
    {
        var provider = new OracleDistributedLockProvider(() => new FakeDbConnection(), _logger);
        var act = () => provider.TryAcquireHandleAsync(resourceId!, TimeSpan.FromSeconds(5));
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task AcquireHandleAsync_InvalidResourceId_ThrowsArgumentException(string? resourceId)
    {
        var provider = new OracleDistributedLockProvider(() => new FakeDbConnection(), _logger);
        var act = () => provider.AcquireHandleAsync(resourceId!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    #endregion
}
