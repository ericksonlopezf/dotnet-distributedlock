// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Sqlite;
using EricksonLopez.Result;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EricksonLopez.DistributedLock.Sqlite.Tests;

public sealed class SqliteDistributedLockProviderTests
{
    private readonly ILogger<SqliteDistributedLockProvider> _logger = NullLogger<SqliteDistributedLockProvider>.Instance;

    #region Constructor & Validation Tests

    [Fact]
    public void Constructor_NullConnection_ThrowsArgumentNullException()
    {
        var act = () => new SqliteDistributedLockProvider((DbConnection)null!, _logger);
        act.Should().Throw<ArgumentNullException>().WithParameterName("connection");
    }

    [Fact]
    public void Constructor_NullConnectionFactory_ThrowsArgumentNullException()
    {
        var act = () => new SqliteDistributedLockProvider((Func<DbConnection>)null!, _logger);
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_NullLoggerWithConnection_ThrowsArgumentNullException()
    {
        var conn = new FakeDbConnection();
        var act = () => new SqliteDistributedLockProvider(conn, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_NullLoggerWithFactory_ThrowsArgumentNullException()
    {
        var act = () => new SqliteDistributedLockProvider(() => new FakeDbConnection(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithOptionsWrapper_InitializesCorrectly()
    {
        var options = Options.Create(new SqliteLockOptions { CommandTimeoutSeconds = 40 });
        var provider = new SqliteDistributedLockProvider(() => new FakeDbConnection(), _logger, options);
        provider.Should().NotBeNull();
    }

    [Fact]
    public async Task Constructor_WithConnectionAndOptions_UsesCustomOptions()
    {
        var conn = new FakeDbConnection();
        FakeDbCommand? executedCmd = null;
        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            executedCmd = cmd;
            return Task.FromResult(1);
        };

        var options = new SqliteLockOptions { CommandTimeoutSeconds = 88 };
        var provider = new SqliteDistributedLockProvider(conn, _logger, options);
        var result = await provider.TryAcquireHandleAsync("custom-opt-res");

        result.IsSuccess.Should().BeTrue();
        executedCmd.Should().NotBeNull();
        executedCmd!.CommandTimeout.Should().Be(88);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task TryAcquireAsync_InvalidResourceId_ThrowsArgumentException(string? resourceId)
    {
        var provider = new SqliteDistributedLockProvider(new FakeDbConnection(), _logger);
        var act = () => provider.TryAcquireAsync(resourceId!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task TryAcquireAsync_WithTimeout_InvalidResourceId_ThrowsArgumentException(string? resourceId)
    {
        var provider = new SqliteDistributedLockProvider(new FakeDbConnection(), _logger);
        var act = () => provider.TryAcquireAsync(resourceId!, TimeSpan.FromSeconds(5));
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task TryAcquireAsync_WithTimeout_NegativeTimeout_ThrowsArgumentOutOfRangeException()
    {
        var provider = new SqliteDistributedLockProvider(new FakeDbConnection(), _logger);
        var act = () => provider.TryAcquireAsync("res-neg", TimeSpan.FromMilliseconds(-1));
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("timeout");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task AcquireAsync_InvalidResourceId_ThrowsArgumentException(string? resourceId)
    {
        var provider = new SqliteDistributedLockProvider(new FakeDbConnection(), _logger);
        var act = () => provider.AcquireAsync(resourceId!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task TryAcquireHandleAsync_InvalidResourceId_ThrowsArgumentException(string? resourceId)
    {
        var provider = new SqliteDistributedLockProvider(new FakeDbConnection(), _logger);
        var act = () => provider.TryAcquireHandleAsync(resourceId!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task TryAcquireHandleAsync_WithTimeout_InvalidResourceId_ThrowsArgumentException(string? resourceId)
    {
        var provider = new SqliteDistributedLockProvider(new FakeDbConnection(), _logger);
        var act = () => provider.TryAcquireHandleAsync(resourceId!, TimeSpan.FromSeconds(5));
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_NegativeTimeout_ThrowsArgumentOutOfRangeException()
    {
        var provider = new SqliteDistributedLockProvider(new FakeDbConnection(), _logger);
        var act = () => provider.TryAcquireHandleAsync("res-neg", TimeSpan.FromSeconds(-5));
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>().WithParameterName("timeout");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task AcquireHandleAsync_InvalidResourceId_ThrowsArgumentException(string? resourceId)
    {
        var provider = new SqliteDistributedLockProvider(new FakeDbConnection(), _logger);
        var act = () => provider.AcquireHandleAsync(resourceId!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    #endregion

    #region Connection Factory (Dedicated Session) Mode

    [Fact]
    public async Task TryAcquireHandleAsync_DedicatedMode_Success_InitializesTableAndInsertsLock()
    {
        FakeDbConnection? createdConn = null;
        var commands = new System.Collections.Generic.List<FakeDbCommand>();

        var provider = new SqliteDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.SetState(ConnectionState.Closed);
                createdConn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
                {
                    commands.Add(cmd);
                    return Task.FromResult(1);
                };
                return createdConn;
            },
            _logger,
            new SqliteLockOptions { CommandTimeoutSeconds = 25 });

        var result = await provider.TryAcquireHandleAsync("order-invoice-101");

        result.IsSuccess.Should().BeTrue();
        result.Value.ResourceId.Should().Be("order-invoice-101");

        createdConn.Should().NotBeNull();
        createdConn!.OpenAsyncCallCount.Should().Be(1);

        commands.Should().HaveCount(2);
        commands[0].CommandText.Should().Contain("CREATE TABLE IF NOT EXISTS __distributed_locks");
        commands[0].CommandTimeout.Should().Be(25);
        commands[1].CommandText.Should().Contain("INSERT INTO __distributed_locks");
        commands[1].Parameters["@ResourceId"]!.ParameterName.Should().Be("@ResourceId");
        commands[1].Parameters["@ResourceId"]!.Value.Should().Be("order-invoice-101");
        commands[1].Parameters["@OwnerId"]!.ParameterName.Should().Be("@OwnerId");
        commands[1].Parameters["@OwnerId"]!.Value!.ToString()!.Length.Should().Be(32);

        await result.Value.DisposeAsync();
        createdConn.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_DedicatedMode_SubsequentCall_SkipsTableCreation()
    {
        var commands = new System.Collections.Generic.List<FakeDbCommand>();

        var provider = new SqliteDistributedLockProvider(
            () =>
            {
                var conn = new FakeDbConnection();
                conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
                {
                    commands.Add(cmd);
                    return Task.FromResult(1);
                };
                return conn;
            },
            _logger);

        var first = await provider.TryAcquireHandleAsync("first-res");
        first.IsSuccess.Should().BeTrue();

        var second = await provider.TryAcquireHandleAsync("second-res");
        second.IsSuccess.Should().BeTrue();

        // First call executed CREATE TABLE + INSERT (2 commands).
        // Second call should only execute INSERT (1 command).
        commands.Should().HaveCount(3);
        commands[0].CommandText.Should().Contain("CREATE TABLE");
        commands[1].CommandText.Should().Contain("INSERT INTO");
        commands[2].CommandText.Should().Contain("INSERT INTO");
    }

    [Fact]
    public async Task TryAcquireHandleAsync_DedicatedMode_ConstraintViolation_ReturnsLockAlreadyHeldAndDisposes()
    {
        FakeDbConnection? createdConn = null;

        var provider = new SqliteDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
                {
                    if (cmd.CommandText.Contains("INSERT INTO"))
                    {
                        throw new SqliteException("UNIQUE constraint failed: __distributed_locks.resource_id", 19);
                    }
                    return Task.FromResult(1);
                };
                return createdConn;
            },
            _logger);

        var result = await provider.TryAcquireHandleAsync("held-res");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
        createdConn!.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_DedicatedMode_PreCanceled_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        FakeDbConnection? createdConn = null;
        var provider = new SqliteDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                return createdConn;
            },
            _logger);

        var result = await provider.TryAcquireHandleAsync("canceled-res", cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
        createdConn.Should().BeNull();
    }

    [Fact]
    public async Task TryAcquireHandleAsync_DedicatedMode_OperationCanceledException_ReturnsCanceledAndDisposes()
    {
        using var cts = new CancellationTokenSource();
        FakeDbConnection? createdConn = null;

        var provider = new SqliteDistributedLockProvider(
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

        var result = await provider.TryAcquireHandleAsync("cancel-op-res", cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
        createdConn!.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_DedicatedMode_UnexpectedException_RethrowsAndDisposes()
    {
        FakeDbConnection? createdConn = null;

        var provider = new SqliteDistributedLockProvider(
            () =>
            {
                createdConn = new FakeDbConnection();
                createdConn.ExecuteNonQueryAsyncHandler = (_, _) => throw new InvalidOperationException("Fatal SQLite database corruption");
                return createdConn;
            },
            _logger);

        var act = () => provider.TryAcquireHandleAsync("fatal-res");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Fatal SQLite database corruption*");

        createdConn!.DisposeAsyncCallCount.Should().Be(1);
    }

    #endregion

    #region Ambient (Existing Connection) Mode

    [Fact]
    public async Task TryAcquireHandleAsync_AmbientMode_Success_ReturnsAmbientLockHandle()
    {
        var conn = new FakeDbConnection();
        var commands = new System.Collections.Generic.List<FakeDbCommand>();

        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            commands.Add(cmd);
            return Task.FromResult(1);
        };

        var provider = new SqliteDistributedLockProvider(conn, _logger);
        var result = await provider.TryAcquireHandleAsync("ambient-res-1");

        result.IsSuccess.Should().BeTrue();
        result.Value.ResourceId.Should().Be("ambient-res-1");
        result.Value.HandleLostToken.Should().Be(CancellationToken.None);

        commands.Should().HaveCount(2);
        commands[0].CommandText.Should().Contain("CREATE TABLE");
        commands[1].CommandText.Should().Contain("INSERT INTO");
        commands[1].Parameters["@ResourceId"]!.ParameterName.Should().Be("@ResourceId");
        commands[1].Parameters["@ResourceId"]!.Value.Should().Be("ambient-res-1");
        commands[1].Parameters["@OwnerId"]!.ParameterName.Should().Be("@OwnerId");
        commands[1].Parameters["@OwnerId"]!.Value!.ToString()!.Length.Should().Be(32);

        // Dispose ambient handle: should execute DELETE without disposing connection
        await result.Value.DisposeAsync();
        commands.Should().HaveCount(3);
        commands[2].CommandText.Should().Contain("DELETE FROM __distributed_locks");
        commands[2].Parameters["@ResourceId"]!.ParameterName.Should().Be("@ResourceId");
        commands[2].Parameters["@ResourceId"]!.Value.Should().Be("ambient-res-1");
        commands[2].Parameters["@OwnerId"]!.ParameterName.Should().Be("@OwnerId");
        commands[2].Parameters["@OwnerId"]!.Value!.ToString()!.Length.Should().Be(32);
        conn.DisposeAsyncCallCount.Should().Be(0);

        // Idempotent dispose
        await result.Value.DisposeAsync();
        commands.Should().HaveCount(3);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_AmbientMode_ConstraintViolation_ReturnsLockAlreadyHeld()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            if (cmd.CommandText.Contains("INSERT INTO"))
            {
                throw new SqliteException("UNIQUE constraint failed", 19);
            }
            return Task.FromResult(1);
        };

        var provider = new SqliteDistributedLockProvider(conn, _logger);
        var result = await provider.TryAcquireHandleAsync("ambient-busy");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_AmbientMode_PreCanceled_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var conn = new FakeDbConnection();
        var provider = new SqliteDistributedLockProvider(conn, _logger);

        var result = await provider.TryAcquireHandleAsync("ambient-canceled", cts.Token);
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

        var provider = new SqliteDistributedLockProvider(conn, _logger);
        var result = await provider.TryAcquireHandleAsync("ambient-cancel-op", cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task AmbientLockHandle_DisposeAsync_WhenConnectionClosed_SkipsDelete()
    {
        var conn = new FakeDbConnection();
        var commands = new System.Collections.Generic.List<FakeDbCommand>();

        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            commands.Add(cmd);
            return Task.FromResult(1);
        };

        var provider = new SqliteDistributedLockProvider(conn, _logger);
        var result = await provider.TryAcquireHandleAsync("ambient-closed-test");
        result.IsSuccess.Should().BeTrue();

        conn.SetState(ConnectionState.Closed);
        await result.Value.DisposeAsync();

        // Only the initial 2 commands (CREATE + INSERT) should have run
        commands.Should().HaveCount(2);
    }

    [Fact]
    public async Task AmbientLockHandle_DisposeAsync_WhenExceptionThrown_LogsWarningAndDoesNotThrow()
    {
        var conn = new FakeDbConnection();
        var deleteFailed = false;

        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            if (cmd.CommandText.Contains("DELETE FROM"))
            {
                deleteFailed = true;
                throw new InvalidOperationException("Delete failed");
            }
            return Task.FromResult(1);
        };

        var provider = new SqliteDistributedLockProvider(conn, _logger);
        var result = await provider.TryAcquireHandleAsync("ambient-err-test");
        result.IsSuccess.Should().BeTrue();

        var act = () => result.Value.DisposeAsync().AsTask();
        await act.Should().NotThrowAsync();
        deleteFailed.Should().BeTrue();
    }

    #endregion

    #region Timeout & Retry Backoff Tests

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_ImmediateSuccess_ReturnsHandle()
    {
        var conn = new FakeDbConnection();
        var provider = new SqliteDistributedLockProvider(() => conn, _logger);

        var result = await provider.TryAcquireHandleAsync("to-instant", TimeSpan.FromSeconds(2));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireHandleAsync_ZeroTimeout_Success_ReturnsHandle()
    {
        var conn = new FakeDbConnection();
        var provider = new SqliteDistributedLockProvider(() => conn, _logger);

        var result = await provider.TryAcquireHandleAsync("to-zero-succ", TimeSpan.Zero);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireHandleAsync_ZeroTimeout_WhenHeld_ReturnsTimeoutImmediately()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            if (cmd.CommandText.Contains("INSERT INTO"))
            {
                throw new SqliteException("busy", 19);
            }
            return Task.FromResult(1);
        };

        var provider = new SqliteDistributedLockProvider(() => conn, _logger);
        var result = await provider.TryAcquireHandleAsync("to-zero-held", TimeSpan.Zero);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Timeout.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_PreCanceledToken_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var conn = new FakeDbConnection();
        var provider = new SqliteDistributedLockProvider(() => conn, _logger);

        var result = await provider.TryAcquireHandleAsync("to-precancel", TimeSpan.FromSeconds(5), cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_RetriesUntilSuccess()
    {
        var attempts = 0;
        var provider = new SqliteDistributedLockProvider(
            () =>
            {
                var conn = new FakeDbConnection();
                conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
                {
                    if (cmd.CommandText.Contains("INSERT INTO"))
                    {
                        attempts++;
                        if (attempts < 3)
                        {
                            throw new SqliteException("busy", 19);
                        }
                    }
                    return Task.FromResult(1);
                };
                return conn;
            },
            _logger,
            new SqliteLockOptions
            {
                RetryInterval = TimeSpan.FromMilliseconds(10),
                BackoffJitter = false
            });

        var result = await provider.TryAcquireHandleAsync("to-retry-succ", TimeSpan.FromSeconds(2));

        result.IsSuccess.Should().BeTrue();
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_TimesOutAfterExhaustion()
    {
        var attempts = 0;
        var provider = new SqliteDistributedLockProvider(
            () =>
            {
                var conn = new FakeDbConnection();
                conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
                {
                    if (cmd.CommandText.Contains("INSERT INTO"))
                    {
                        attempts++;
                        throw new SqliteException("busy", 19);
                    }
                    return Task.FromResult(1);
                };
                return conn;
            },
            _logger,
            new SqliteLockOptions
            {
                RetryInterval = TimeSpan.FromMilliseconds(15),
                BackoffJitter = true
            });

        var result = await provider.TryAcquireHandleAsync("to-exhaust", TimeSpan.FromMilliseconds(50));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Timeout.Code);
        attempts.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithZeroRetryInterval_DefaultsToFiftyMs()
    {
        var attempts = 0;
        var provider = new SqliteDistributedLockProvider(
            () =>
            {
                var conn = new FakeDbConnection();
                conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
                {
                    if (cmd.CommandText.Contains("INSERT INTO"))
                    {
                        attempts++;
                        throw new SqliteException("busy", 19);
                    }
                    return Task.FromResult(1);
                };
                return conn;
            },
            _logger,
            new SqliteLockOptions
            {
                RetryInterval = TimeSpan.Zero,
                BackoffJitter = false
            });

        var result = await provider.TryAcquireHandleAsync("to-zero-interval", TimeSpan.FromMilliseconds(25));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Timeout.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_CanceledDuringLoop_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        var provider = new SqliteDistributedLockProvider(
            () =>
            {
                var conn = new FakeDbConnection();
                conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
                {
                    if (cmd.CommandText.Contains("INSERT INTO"))
                    {
                        cts.Cancel();
                        throw new SqliteException("busy", 19);
                    }
                    return Task.FromResult(1);
                };
                return conn;
            },
            _logger,
            new SqliteLockOptions
            {
                RetryInterval = TimeSpan.FromMilliseconds(10)
            });

        var result = await provider.TryAcquireHandleAsync("to-cancel-loop", TimeSpan.FromSeconds(5), cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    #endregion

    #region Blocking AcquireHandleAsync Tests

    [Fact]
    public async Task AcquireHandleAsync_ImmediateSuccess_ReturnsHandle()
    {
        var conn = new FakeDbConnection();
        var provider = new SqliteDistributedLockProvider(() => conn, _logger);

        var result = await provider.AcquireHandleAsync("acq-instant");

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task AcquireHandleAsync_PreCanceled_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var conn = new FakeDbConnection();
        var provider = new SqliteDistributedLockProvider(() => conn, _logger);

        var result = await provider.AcquireHandleAsync("acq-precancel", cts.Token);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task AcquireHandleAsync_CancelledDuringDelay_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        var attempts = 0;
        var provider = new SqliteDistributedLockProvider(
            () =>
            {
                var conn = new FakeDbConnection();
                conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
                {
                    if (cmd.CommandText.Contains("INSERT INTO"))
                    {
                        attempts++;
                        cts.Cancel();
                        throw new SqliteException("busy", 19);
                    }
                    return Task.FromResult(1);
                };
                return conn;
            },
            _logger,
            new SqliteLockOptions { RetryInterval = TimeSpan.FromMilliseconds(50), BackoffJitter = false });

        var result = await provider.AcquireHandleAsync("acq-cancel-delay", cts.Token);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task AcquireHandleAsync_RetriesUntilSuccess()
    {
        var attempts = 0;
        var provider = new SqliteDistributedLockProvider(
            () =>
            {
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
                return conn;
            },
            _logger,
            new SqliteLockOptions
            {
                RetryInterval = TimeSpan.FromMilliseconds(10),
                BackoffJitter = false
            });

        var result = await provider.AcquireHandleAsync("acq-retry");

        result.IsSuccess.Should().BeTrue();
        attempts.Should().Be(2);
    }

    [Fact]
    public async Task AcquireHandleAsync_WithZeroRetryInterval_UsesDefaultFiftyMs()
    {
        var attempts = 0;
        var provider = new SqliteDistributedLockProvider(
            () =>
            {
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
                return conn;
            },
            _logger,
            new SqliteLockOptions
            {
                RetryInterval = TimeSpan.Zero,
                BackoffJitter = true
            });

        var result = await provider.AcquireHandleAsync("acq-zero-interval");

        result.IsSuccess.Should().BeTrue();
        attempts.Should().Be(2);
    }

    #endregion

    #region TryAcquireAsync & AcquireAsync (IAsyncDisposable Wrappers) Tests

    [Fact]
    public async Task TryAcquireAsync_WhenSuccessful_ReturnsDisposableResult()
    {
        var conn = new FakeDbConnection();
        var provider = new SqliteDistributedLockProvider(() => conn, _logger);

        var result = await provider.TryAcquireAsync("disp-succ");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_WhenFailed_ReturnsFailureResult()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            if (cmd.CommandText.Contains("INSERT INTO"))
            {
                throw new SqliteException("busy", 19);
            }
            return Task.FromResult(1);
        };

        var provider = new SqliteDistributedLockProvider(() => conn, _logger);
        var result = await provider.TryAcquireAsync("disp-fail");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task TryAcquireAsync_WithTimeout_WhenSuccessful_ReturnsDisposableResult()
    {
        var conn = new FakeDbConnection();
        var provider = new SqliteDistributedLockProvider(() => conn, _logger);

        var result = await provider.TryAcquireAsync("disp-to-succ", TimeSpan.FromSeconds(2));

        result.IsSuccess.Should().BeTrue();
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_WithTimeout_WhenFailed_ReturnsFailureResult()
    {
        var conn = new FakeDbConnection();
        conn.ExecuteNonQueryAsyncHandler = (cmd, _) =>
        {
            if (cmd.CommandText.Contains("INSERT INTO"))
            {
                throw new SqliteException("busy", 19);
            }
            return Task.FromResult(1);
        };

        var provider = new SqliteDistributedLockProvider(() => conn, _logger);
        var result = await provider.TryAcquireAsync("disp-to-fail", TimeSpan.Zero);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Timeout.Code);
    }

    [Fact]
    public async Task AcquireAsync_WhenSuccessful_ReturnsDisposableResult()
    {
        var conn = new FakeDbConnection();
        var provider = new SqliteDistributedLockProvider(() => conn, _logger);

        var result = await provider.AcquireAsync("disp-acq-succ");

        result.IsSuccess.Should().BeTrue();
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task AcquireAsync_WhenCanceled_ReturnsFailureResult()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var conn = new FakeDbConnection();
        var provider = new SqliteDistributedLockProvider(() => conn, _logger);

        var result = await provider.AcquireAsync("disp-acq-precancel", cts.Token);
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    #endregion
}
