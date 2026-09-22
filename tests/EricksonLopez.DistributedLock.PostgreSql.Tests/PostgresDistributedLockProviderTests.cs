// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.PostgreSql;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.DistributedLock.PostgreSql.Tests;

public sealed class PostgresDistributedLockProviderTests
{
    #region Constructor & Guard Tests

    [Fact]
    public void Constructor_NullConnection_ThrowsArgumentNullException()
    {
        var act = () => new PostgresDistributedLockProvider((IDbConnection)null!, NullLogger<PostgresDistributedLockProvider>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("connection");
    }

    [Fact]
    public void Constructor_NullConnectionFactory_ThrowsArgumentNullException()
    {
        var act = () => new PostgresDistributedLockProvider((Func<DbConnection>)null!, NullLogger<PostgresDistributedLockProvider>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var connection = new FakeDbConnection();
        var act1 = () => new PostgresDistributedLockProvider(connection, null!);
        act1.Should().Throw<ArgumentNullException>().WithParameterName("logger");

        var act2 = () => new PostgresDistributedLockProvider(() => connection, null!);
        act2.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_WithOptions_InitializesCorrectly()
    {
        var options = new PostgresLockOptions { CommandTimeoutSeconds = 42 };
        var provider = new PostgresDistributedLockProvider(
            () => new FakeDbConnection(),
            NullLogger<PostgresDistributedLockProvider>.Instance,
            options);

        provider.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithIOptions_InitializesCorrectly()
    {
        var options = Options.Create(new PostgresLockOptions { CommandTimeoutSeconds = 50 });
        var provider = new PostgresDistributedLockProvider(
            () => new FakeDbConnection(),
            NullLogger<PostgresDistributedLockProvider>.Instance,
            options);

        provider.Should().NotBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task TryAcquireAsync_InvalidResourceId_ThrowsArgumentException(string? resourceId)
    {
        var connection = new FakeDbConnection();
        var provider = new PostgresDistributedLockProvider(connection, NullLogger<PostgresDistributedLockProvider>.Instance);

        var act = () => provider.TryAcquireAsync(resourceId!);
        await act.Should().ThrowAsync<ArgumentException>();

        var actTimeout = () => provider.TryAcquireAsync(resourceId!, TimeSpan.FromSeconds(5));
        await actTimeout.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public async Task AcquireAsync_InvalidResourceId_ThrowsArgumentException(string? resourceId)
    {
        var connection = new FakeDbConnection();
        var provider = new PostgresDistributedLockProvider(connection, NullLogger<PostgresDistributedLockProvider>.Instance);

        var act = () => provider.AcquireAsync(resourceId!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void GenerateLockId_InvalidResourceId_ThrowsArgumentException(string? resourceId)
    {
        var act = () => PostgresDistributedLockProvider.GenerateLockId(resourceId!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GenerateLockId_GeneratesDeterministic64BitHashes()
    {
        const string resource = "invoices:2026:january";
        var id1 = PostgresDistributedLockProvider.GenerateLockId(resource);
        var id2 = PostgresDistributedLockProvider.GenerateLockId(resource);

        id1.Should().Be(id2);
        id1.Should().NotBe(0);

        var differentId = PostgresDistributedLockProvider.GenerateLockId("invoices:2026:february");
        differentId.Should().NotBe(id1);
    }

    #endregion

    #region Session-Level Locking Tests

    [Fact]
    public async Task Session_TryAcquireAsync_WhenClosedConnection_OpensConnectionAndReturnsHandle()
    {
        var connection = new FakeDbConnection();
        connection.SetState(ConnectionState.Closed);
        var executedSql = string.Empty;

        connection.ExecuteScalarAsyncHandler = (cmd, ct) =>
        {
            executedSql = cmd.CommandText;
            return Task.FromResult<object?>(true);
        };

        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance,
            new PostgresLockOptions { CommandTimeoutSeconds = 30 });

        var result = await provider.TryAcquireAsync("orders:session-1");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<PostgresAdvisoryLockHandle>();
        connection.OpenAsyncCallCount.Should().Be(1);
        executedSql.Should().Be("SELECT pg_try_advisory_lock(@LockId);");
        connection.CommandsCreated.Should().ContainSingle(c => c.CommandTimeout == 30);

        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task Session_TryAcquireAsync_WhenAlreadyHeld_ReturnsLockAlreadyHeldAndDisposesConnection()
    {
        var connection = new FakeDbConnection();
        connection.ExecuteScalarAsyncHandler = (cmd, ct) => Task.FromResult<object?>(false);

        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.TryAcquireAsync("orders:held");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
        connection.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Session_TryAcquireAsync_WhenCanceled_ReturnsCanceledResultAndDisposesConnection()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var connection = new FakeDbConnection();
        connection.ExecuteScalarAsyncHandler = (cmd, ct) => throw new OperationCanceledException(ct);

        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.TryAcquireAsync("orders:cancel", cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
        connection.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Session_TryAcquireAsync_WhenQueryThrows_LogsErrorDisposesConnectionAndRethrows()
    {
        var connection = new FakeDbConnection();
        connection.ExecuteScalarAsyncHandler = (cmd, ct) => throw new InvalidOperationException("PG connection severed");

        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var act = () => provider.TryAcquireAsync("orders:error");
        await act.Should().ThrowAsync<InvalidOperationException>();

        connection.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Session_TryAcquireAsync_WithZeroOrNegativeTimeout_DelegatesToImmediateTryAcquire()
    {
        var connection = new FakeDbConnection();
        connection.ExecuteScalarAsyncHandler = (cmd, ct) => Task.FromResult<object?>(true);

        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.TryAcquireAsync("orders:zero-timeout", TimeSpan.Zero);

        result.IsSuccess.Should().BeTrue();
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task Session_TryAcquireAsync_WithTimeout_WhenAcquiredAfterPolling_ReturnsSuccess()
    {
        var connection = new FakeDbConnection();
        var attempts = 0;
        connection.ExecuteScalarAsyncHandler = (cmd, ct) =>
        {
            attempts++;
            return Task.FromResult<object?>(attempts >= 2);
        };

        var options = new PostgresLockOptions
        {
            InitialPollingInterval = TimeSpan.FromMilliseconds(5),
            MaxPollingInterval = TimeSpan.FromMilliseconds(10),
            JitterRatio = 0.1
        };

        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance,
            options);

        var result = await provider.TryAcquireAsync("orders:polling-success", TimeSpan.FromSeconds(2));

        result.IsSuccess.Should().BeTrue();
        attempts.Should().BeGreaterOrEqualTo(2);
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task Session_TryAcquireAsync_WithTimeout_WhenTimeoutExpires_ReturnsTimeout()
    {
        var connection = new FakeDbConnection();
        connection.ExecuteScalarAsyncHandler = (cmd, ct) => Task.FromResult<object?>(false);

        var options = new PostgresLockOptions
        {
            InitialPollingInterval = TimeSpan.FromMilliseconds(5),
            MaxPollingInterval = TimeSpan.FromMilliseconds(10),
            JitterRatio = 0.0
        };

        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance,
            options);

        var result = await provider.TryAcquireAsync("orders:polling-timeout", TimeSpan.FromMilliseconds(30));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Timeout.Code);
        connection.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Session_TryAcquireAsync_WithTimeout_WhenTokenCanceledBeforeLoop_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var connection = new FakeDbConnection();
        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.TryAcquireAsync("orders:polling-cancel-early", TimeSpan.FromSeconds(1), cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
        connection.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Session_TryAcquireAsync_WithTimeout_WhenCanceledDuringPollingDelay_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        var connection = new FakeDbConnection();
        var attempts = 0;

        connection.ExecuteScalarAsyncHandler = (cmd, ct) =>
        {
            attempts++;
            cts.Cancel();
            return Task.FromResult<object?>(false);
        };

        var options = new PostgresLockOptions
        {
            InitialPollingInterval = TimeSpan.FromMilliseconds(200)
        };

        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance,
            options);

        var result = await provider.TryAcquireAsync("orders:polling-cancel-delay", TimeSpan.FromSeconds(2), cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
        connection.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Session_TryAcquireAsync_WithTimeout_WhenQueryThrows_LogsErrorAndRethrows()
    {
        var connection = new FakeDbConnection();
        connection.ExecuteScalarAsyncHandler = (cmd, ct) => throw new InvalidOperationException("Fatal database error");

        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var act = () => provider.TryAcquireAsync("orders:polling-throw", TimeSpan.FromSeconds(2));
        await act.Should().ThrowAsync<InvalidOperationException>();

        connection.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Session_AcquireAsync_WhenClosedConnection_OpensConnectionAndReturnsHandle()
    {
        var connection = new FakeDbConnection();
        connection.SetState(ConnectionState.Closed);
        var executedSql = string.Empty;

        connection.ExecuteNonQueryAsyncHandler = (cmd, ct) =>
        {
            executedSql = cmd.CommandText;
            return Task.FromResult(1);
        };

        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.AcquireAsync("orders:blocking-session");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<PostgresAdvisoryLockHandle>();
        connection.OpenAsyncCallCount.Should().Be(1);
        executedSql.Should().Be("SELECT pg_advisory_lock(@LockId);");

        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task Session_AcquireAsync_WhenOperationCanceled_ReturnsCanceledResult()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var connection = new FakeDbConnection();
        connection.ExecuteNonQueryAsyncHandler = (cmd, ct) => throw new OperationCanceledException(ct);

        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.AcquireAsync("orders:blocking-cancel", cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
        connection.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Session_AcquireAsync_WhenExceptionWithCancellationRequested_ReturnsCanceledResult()
    {
        using var cts = new CancellationTokenSource();
        var connection = new FakeDbConnection();

        connection.ExecuteNonQueryAsyncHandler = (cmd, ct) =>
        {
            cts.Cancel();
            throw new InvalidOperationException("Connection broken during lock wait");
        };

        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.AcquireAsync("orders:blocking-ex-cancel", cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
        connection.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Session_AcquireAsync_WhenUnexpectedExceptionOccurs_DisposesConnectionAndRethrows()
    {
        var connection = new FakeDbConnection();
        connection.ExecuteNonQueryAsyncHandler = (cmd, ct) => throw new InvalidOperationException("Fatal unexpected error");

        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var act = () => provider.AcquireAsync("orders:blocking-error");
        await act.Should().ThrowAsync<InvalidOperationException>();

        connection.DisposeAsyncCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Session_AcquireAsync_WithInfiniteTimeout_DelegatesToUnboundedAcquireAsync()
    {
        var connection = new FakeDbConnection();
        connection.ExecuteNonQueryAsyncHandler = (cmd, ct) => Task.FromResult(1);

        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.AcquireAsync("orders:infinite", Timeout.InfiniteTimeSpan);

        result.IsSuccess.Should().BeTrue();
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task Session_AcquireAsync_WithBoundedTimeout_DelegatesToTryAcquireAsyncWithTimeout()
    {
        var connection = new FakeDbConnection();
        connection.ExecuteScalarAsyncHandler = (cmd, ct) => Task.FromResult<object?>(true);

        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.AcquireAsync("orders:bounded-timeout", TimeSpan.FromSeconds(2));

        result.IsSuccess.Should().BeTrue();
        await result.Value.DisposeAsync();
    }

    #endregion

    #region Transaction-Bound Locking Tests

    [Fact]
    public async Task Transaction_TryAcquireAsync_WhenAcquired_ReturnsSuccessNoOpHandle()
    {
        var connection = new FakeDbConnection();
        var executedSql = string.Empty;

        connection.ExecuteScalarAsyncHandler = (cmd, ct) =>
        {
            executedSql = cmd.CommandText;
            return Task.FromResult<object?>(true);
        };

        var provider = new PostgresDistributedLockProvider(
            connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.TryAcquireAsync("invoice:xact-success");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<NoOpAsyncDisposable>();
        executedSql.Should().Be("SELECT pg_try_advisory_xact_lock(@LockId);");
    }

    [Fact]
    public async Task Transaction_TryAcquireAsync_WhenAlreadyHeld_ReturnsLockAlreadyHeld()
    {
        var connection = new FakeDbConnection();
        connection.ExecuteScalarAsyncHandler = (cmd, ct) => Task.FromResult<object?>(false);

        var provider = new PostgresDistributedLockProvider(
            connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.TryAcquireAsync("invoice:xact-held");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task Transaction_TryAcquireAsync_WhenCanceled_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var connection = new FakeDbConnection();
        connection.ExecuteScalarAsyncHandler = (cmd, ct) => throw new OperationCanceledException(ct);

        var provider = new PostgresDistributedLockProvider(
            connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.TryAcquireAsync("invoice:xact-cancel", cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task Transaction_TryAcquireAsync_WhenQueryThrows_LogsErrorAndRethrows()
    {
        var connection = new FakeDbConnection();
        connection.ExecuteScalarAsyncHandler = (cmd, ct) => throw new InvalidOperationException("Transaction aborted");

        var provider = new PostgresDistributedLockProvider(
            connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var act = () => provider.TryAcquireAsync("invoice:xact-error");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Transaction_TryAcquireAsync_WithTimeout_WhenAcquired_ReturnsSuccess()
    {
        var connection = new FakeDbConnection();
        connection.ExecuteScalarAsyncHandler = (cmd, ct) => Task.FromResult<object?>(true);

        var provider = new PostgresDistributedLockProvider(
            connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.TryAcquireAsync("invoice:xact-timeout-success", TimeSpan.FromSeconds(2));

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Transaction_TryAcquireAsync_WithTimeout_WhenTimeoutExpires_ReturnsTimeout()
    {
        var connection = new FakeDbConnection();
        connection.ExecuteScalarAsyncHandler = (cmd, ct) => Task.FromResult<object?>(false);

        var options = new PostgresLockOptions
        {
            InitialPollingInterval = TimeSpan.FromMilliseconds(5),
            MaxPollingInterval = TimeSpan.FromMilliseconds(10),
            JitterRatio = 0.0
        };

        var provider = new PostgresDistributedLockProvider(
            connection,
            NullLogger<PostgresDistributedLockProvider>.Instance,
            options);

        var result = await provider.TryAcquireAsync("invoice:xact-timeout-fail", TimeSpan.FromMilliseconds(30));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Timeout.Code);
    }

    [Fact]
    public async Task Transaction_TryAcquireAsync_WithTimeout_WhenTokenCanceledEarly_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var connection = new FakeDbConnection();
        var provider = new PostgresDistributedLockProvider(
            connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.TryAcquireAsync("invoice:xact-cancel-early", TimeSpan.FromSeconds(1), cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task Transaction_TryAcquireAsync_WithTimeout_WhenCanceledDuringPollingDelay_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        var connection = new FakeDbConnection();

        connection.ExecuteScalarAsyncHandler = (cmd, ct) =>
        {
            cts.Cancel();
            return Task.FromResult<object?>(false);
        };

        var options = new PostgresLockOptions
        {
            InitialPollingInterval = TimeSpan.FromMilliseconds(200)
        };

        var provider = new PostgresDistributedLockProvider(
            connection,
            NullLogger<PostgresDistributedLockProvider>.Instance,
            options);

        var result = await provider.TryAcquireAsync("invoice:xact-cancel-delay", TimeSpan.FromSeconds(2), cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task Transaction_TryAcquireAsync_WithTimeout_WhenQueryThrows_LogsErrorAndRethrows()
    {
        var connection = new FakeDbConnection();
        connection.ExecuteScalarAsyncHandler = (cmd, ct) => throw new InvalidOperationException("Fatal error");

        var provider = new PostgresDistributedLockProvider(
            connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var act = () => provider.TryAcquireAsync("invoice:xact-throw", TimeSpan.FromSeconds(1));
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Transaction_AcquireAsync_WhenAcquired_ReturnsSuccessNoOpHandle()
    {
        var connection = new FakeDbConnection();
        var executedSql = string.Empty;

        connection.ExecuteNonQueryAsyncHandler = (cmd, ct) =>
        {
            executedSql = cmd.CommandText;
            return Task.FromResult(1);
        };

        var provider = new PostgresDistributedLockProvider(
            connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.AcquireAsync("invoice:blocking-xact");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeOfType<NoOpAsyncDisposable>();
        executedSql.Should().Be("SELECT pg_advisory_xact_lock(@LockId);");
    }

    [Fact]
    public async Task Transaction_AcquireAsync_WhenOperationCanceled_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var connection = new FakeDbConnection();
        connection.ExecuteNonQueryAsyncHandler = (cmd, ct) => throw new OperationCanceledException(ct);

        var provider = new PostgresDistributedLockProvider(
            connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.AcquireAsync("invoice:blocking-xact-cancel", cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task Transaction_AcquireAsync_WhenExceptionWithCancellationRequested_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        var connection = new FakeDbConnection();

        connection.ExecuteNonQueryAsyncHandler = (cmd, ct) =>
        {
            cts.Cancel();
            throw new InvalidOperationException("Error during acquire");
        };

        var provider = new PostgresDistributedLockProvider(
            connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.AcquireAsync("invoice:blocking-xact-cancel-ex", cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task Transaction_AcquireAsync_WhenUnexpectedExceptionOccurs_LogsErrorAndRethrows()
    {
        var connection = new FakeDbConnection();
        connection.ExecuteNonQueryAsyncHandler = (cmd, ct) => throw new InvalidOperationException("Catastrophic failure");

        var provider = new PostgresDistributedLockProvider(
            connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var act = () => provider.AcquireAsync("invoice:blocking-xact-error");
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    #endregion

    #region Pure IDbConnection (Sync Fallback) Tests

    [Fact]
    public async Task SyncConnection_TryAcquireAsync_ExecutesSyncScalarAndReturnsSuccess()
    {
        var syncConn = new FakeSyncConnection();
        var executedSql = string.Empty;

        syncConn.ExecuteScalarHandler = cmd =>
        {
            executedSql = cmd.CommandText;
            return true;
        };

        var provider = new PostgresDistributedLockProvider(
            syncConn,
            NullLogger<PostgresDistributedLockProvider>.Instance,
            new PostgresLockOptions { CommandTimeoutSeconds = 25 });

        var result = await provider.TryAcquireAsync("sync:try-acquire");

        result.IsSuccess.Should().BeTrue();
        executedSql.Should().Be("SELECT pg_try_advisory_xact_lock(@LockId);");
        syncConn.CommandsCreated.Should().ContainSingle(c => c.CommandTimeout == 25);
    }

    [Fact]
    public async Task SyncConnection_AcquireAsync_ExecutesSyncNonQueryAndReturnsSuccess()
    {
        var syncConn = new FakeSyncConnection();
        var executedSql = string.Empty;

        syncConn.ExecuteNonQueryHandler = cmd =>
        {
            executedSql = cmd.CommandText;
            return 1;
        };

        var provider = new PostgresDistributedLockProvider(
            syncConn,
            NullLogger<PostgresDistributedLockProvider>.Instance,
            new PostgresLockOptions { CommandTimeoutSeconds = 25 });

        var result = await provider.AcquireAsync("sync:blocking-acquire");

        result.IsSuccess.Should().BeTrue();
        executedSql.Should().Be("SELECT pg_advisory_xact_lock(@LockId);");
        syncConn.CommandsCreated.Should().ContainSingle(c => c.CommandTimeout == 25);
    }

    #endregion

    #region Strongly Typed Handle Methods Tests

    [Fact]
    public async Task TryAcquireHandleAsync_Immediate_WhenSuccessful_ReturnsTypedHandle()
    {
        var connection = new FakeDbConnection();
        connection.ExecuteScalarAsyncHandler = (cmd, ct) => Task.FromResult<object?>(true);

        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.TryAcquireHandleAsync("handle:try-immediate");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeAssignableTo<IDistributedLockHandle>();

        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireHandleAsync_Immediate_WhenFails_ReturnsError()
    {
        var connection = new FakeDbConnection();
        connection.ExecuteScalarAsyncHandler = (cmd, ct) => Task.FromResult<object?>(false);

        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.TryAcquireHandleAsync("handle:try-fail");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.LockAlreadyHeld.Code);
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_WhenSuccessful_ReturnsTypedHandle()
    {
        var connection = new FakeDbConnection();
        connection.ExecuteScalarAsyncHandler = (cmd, ct) => Task.FromResult<object?>(true);

        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.TryAcquireHandleAsync("handle:try-timeout", TimeSpan.FromSeconds(1));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeAssignableTo<IDistributedLockHandle>();

        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireHandleAsync_WithTimeout_WhenFails_ReturnsError()
    {
        var connection = new FakeDbConnection();
        connection.ExecuteScalarAsyncHandler = (cmd, ct) => Task.FromResult<object?>(false);

        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance,
            new PostgresLockOptions { InitialPollingInterval = TimeSpan.FromMilliseconds(5) });

        var result = await provider.TryAcquireHandleAsync("handle:try-timeout-fail", TimeSpan.FromMilliseconds(20));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Timeout.Code);
    }

    [Fact]
    public async Task AcquireHandleAsync_Immediate_WhenSuccessful_ReturnsTypedHandle()
    {
        var connection = new FakeDbConnection();
        connection.ExecuteNonQueryAsyncHandler = (cmd, ct) => Task.FromResult(1);

        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.AcquireHandleAsync("handle:acquire-immediate");

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeAssignableTo<IDistributedLockHandle>();

        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task AcquireHandleAsync_Immediate_WhenCanceled_ReturnsError()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var connection = new FakeDbConnection();
        connection.ExecuteNonQueryAsyncHandler = (cmd, ct) => throw new OperationCanceledException(ct);

        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.AcquireHandleAsync("handle:acquire-cancel", cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task AcquireHandleAsync_WithTimeout_WhenSuccessful_ReturnsTypedHandle()
    {
        var connection = new FakeDbConnection();
        connection.ExecuteScalarAsyncHandler = (cmd, ct) => Task.FromResult<object?>(true);

        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.AcquireHandleAsync("handle:acquire-timeout", TimeSpan.FromSeconds(1));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeAssignableTo<IDistributedLockHandle>();

        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task AcquireHandleAsync_WithTimeout_WhenFails_ReturnsError()
    {
        var connection = new FakeDbConnection();
        connection.ExecuteScalarAsyncHandler = (cmd, ct) => Task.FromResult<object?>(false);

        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance,
            new PostgresLockOptions { InitialPollingInterval = TimeSpan.FromMilliseconds(5) });

        var result = await provider.AcquireHandleAsync("handle:acquire-timeout-fail", TimeSpan.FromMilliseconds(20));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Timeout.Code);
    }

    #endregion

    #region Additional Branch Coverage Tests

    [Fact]
    public async Task Session_TryAcquireAsync_WithTimeout_WhenClosedConnection_OpensConnection()
    {
        var connection = new FakeDbConnection();
        connection.SetState(ConnectionState.Closed);

        connection.ExecuteScalarAsyncHandler = (cmd, ct) => Task.FromResult<object?>(true);

        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.TryAcquireAsync("orders:closed-polling", TimeSpan.FromSeconds(1));

        result.IsSuccess.Should().BeTrue();
        connection.OpenAsyncCallCount.Should().Be(1);
        await result.Value.DisposeAsync();
    }

    [Fact]
    public async Task Session_TryAcquireAsync_WithTimeout_WhenRemainingExpiresDuringLoop_BreaksAndReturnsTimeout()
    {
        var connection = new FakeDbConnection();
        connection.ExecuteScalarAsyncHandler = async (cmd, ct) =>
        {
            await Task.Delay(40, ct);
            return false;
        };

        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.TryAcquireAsync("orders:remaining-expired", TimeSpan.FromMilliseconds(20));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Timeout.Code);
    }

    [Fact]
    public async Task Session_TryAcquireAsync_WithTimeout_WhenOpenAsyncThrowsOperationCanceled_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var connection = new FakeDbConnection();
        connection.SetState(ConnectionState.Closed);
        connection.OpenAsyncHandler = ct => throw new OperationCanceledException(ct);

        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.TryAcquireAsync("orders:open-cancel", TimeSpan.FromSeconds(2), cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task Transaction_TryAcquireAsync_WithTimeout_WhenRemainingExpiresDuringLoop_BreaksAndReturnsTimeout()
    {
        var connection = new FakeDbConnection();
        connection.ExecuteScalarAsyncHandler = async (cmd, ct) =>
        {
            await Task.Delay(40, ct);
            return false;
        };

        var provider = new PostgresDistributedLockProvider(
            connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.TryAcquireAsync("xact:remaining-expired", TimeSpan.FromMilliseconds(20));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Timeout.Code);
    }

    [Fact]
    public async Task Transaction_TryAcquireAsync_WithTimeout_WhenQueryThrowsOperationCanceled_ReturnsCanceled()
    {
        using var cts = new CancellationTokenSource();
        var connection = new FakeDbConnection();

        connection.ExecuteScalarAsyncHandler = (cmd, ct) =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        };

        var provider = new PostgresDistributedLockProvider(
            connection,
            NullLogger<PostgresDistributedLockProvider>.Instance);

        var result = await provider.TryAcquireAsync("xact:query-cancel", TimeSpan.FromSeconds(2), cts.Token);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(DistributedLockErrors.Canceled.Code);
    }

    [Fact]
    public async Task Session_AcquireAsync_WhenCommandTimeoutSpecified_SetsCommandTimeout()
    {
        var connection = new FakeDbConnection();
        connection.ExecuteNonQueryAsyncHandler = (cmd, ct) => Task.FromResult(1);

        var options = new PostgresLockOptions { CommandTimeoutSeconds = 65 };
        var provider = new PostgresDistributedLockProvider(
            () => connection,
            NullLogger<PostgresDistributedLockProvider>.Instance,
            options);

        var result = await provider.AcquireAsync("orders:blocking-timeout-prop");

        result.IsSuccess.Should().BeTrue();
        connection.CommandsCreated.Should().ContainSingle(c => c.CommandTimeout == 65);
        await result.Value.DisposeAsync();
    }

    #endregion
}
