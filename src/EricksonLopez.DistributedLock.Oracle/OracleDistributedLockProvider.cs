// Copyright © Erickson Lopez. MIT License.
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.Result;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EricksonLopez.DistributedLock.Oracle;

/// <summary>
/// Provides distributed locks backed by Oracle Database's DBMS_LOCK package.
/// </summary>
public sealed class OracleDistributedLockProvider : IDistributedLockProvider
{
    private readonly IDbConnection? _connection;
    private readonly Func<DbConnection>? _connectionFactory;
    private readonly ILogger<OracleDistributedLockProvider> _logger;
    private readonly OracleLockOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="OracleDistributedLockProvider"/> class using an existing database connection.
    /// </summary>
    /// <param name="connection">The database connection used for ambient session locks.</param>
    /// <param name="logger">The logger used for recording diagnostic and lifecycle events.</param>
    /// <param name="options">The configuration options for lock acquisition and keepalive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> or <paramref name="logger"/> is <see langword="null"/></exception>
    public OracleDistributedLockProvider(
        IDbConnection connection,
        ILogger<OracleDistributedLockProvider> logger,
        OracleLockOptions? options = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new OracleLockOptions();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OracleDistributedLockProvider"/> class using a connection factory for dedicated sessions.
    /// </summary>
    /// <param name="connectionFactory">The delegate producing dedicated database connections per lock handle.</param>
    /// <param name="logger">The logger used for recording diagnostic and lifecycle events.</param>
    /// <param name="options">The configuration options for lock acquisition and keepalive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> or <paramref name="logger"/> is <see langword="null"/></exception>
    public OracleDistributedLockProvider(
        Func<DbConnection> connectionFactory,
        ILogger<OracleDistributedLockProvider> logger,
        OracleLockOptions? options = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new OracleLockOptions();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OracleDistributedLockProvider"/> class configured via options snapshot.
    /// </summary>
    /// <param name="connectionFactory">The delegate producing dedicated database connections per lock handle.</param>
    /// <param name="logger">The logger used for recording diagnostic and lifecycle events.</param>
    /// <param name="options">The configuration options accessor.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> or <paramref name="logger"/> is <see langword="null"/></exception>
    public OracleDistributedLockProvider(
        Func<DbConnection> connectionFactory,
        ILogger<OracleDistributedLockProvider> logger,
        IOptions<OracleLockOptions> options)
        : this(connectionFactory, logger, options?.Value)
    {
    }

    /// <inheritdoc />
    public async Task<Result<IAsyncDisposable>> TryAcquireAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var handleResult = await TryAcquireHandleAsync(resourceId, cancellationToken).ConfigureAwait(false);
        if (handleResult.IsFailure)
        {
            return Result<IAsyncDisposable>.Failure(handleResult.Error);
        }

        return Result<IAsyncDisposable>.Success(handleResult.Value);
    }

    /// <inheritdoc />
    public async Task<Result<IAsyncDisposable>> TryAcquireAsync(
        string resourceId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var handleResult = await TryAcquireHandleAsync(resourceId, timeout, cancellationToken).ConfigureAwait(false);
        if (handleResult.IsFailure)
        {
            return Result<IAsyncDisposable>.Failure(handleResult.Error);
        }

        return Result<IAsyncDisposable>.Success(handleResult.Value);
    }

    /// <inheritdoc />
    public async Task<Result<IAsyncDisposable>> AcquireAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var handleResult = await AcquireHandleAsync(resourceId, cancellationToken).ConfigureAwait(false);
        if (handleResult.IsFailure)
        {
            return Result<IAsyncDisposable>.Failure(handleResult.Error);
        }

        return Result<IAsyncDisposable>.Success(handleResult.Value);
    }

    /// <inheritdoc />
    public async Task<Result<IDistributedLockHandle>> TryAcquireHandleAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        if (cancellationToken.IsCancellationRequested)
        {
            return DistributedLockErrors.Canceled;
        }

        var lockName = NormalizeLockName(resourceId);

        if (_connectionFactory is not null)
        {
            var connection = _connectionFactory();
            try
            {
                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                }

                var (status, lockHandle) = await ExecuteRequestLockAsync(connection, lockName, 0, releaseOnCommit: false, cancellationToken).ConfigureAwait(false);
                if (status == 0)
                {
                    _logger.LogInformation("Successfully acquired Oracle session lock for resource '{ResourceId}'.", resourceId);
                    DistributedLockMetrics.RecordAcquisition(resourceId, "session", "acquired");
                    return Result<IDistributedLockHandle>.Success(
                        new OracleDistributedLockHandle(connection, lockHandle, resourceId, _logger, _options.KeepaliveCadence, _options.CommandTimeoutSeconds));
                }

                _logger.LogWarning("Failed to acquire Oracle lock for resource '{ResourceId}'. Status: {Status}.", resourceId, status);
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "already_held");
                await connection.DisposeAsync().ConfigureAwait(false);
                return DistributedLockErrors.LockAlreadyHeld;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "canceled");
                await connection.DisposeAsync().ConfigureAwait(false);
                return DistributedLockErrors.Canceled;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error acquiring Oracle lock for resource '{ResourceId}'.", resourceId);
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "error");
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        try
        {
            var (status, lockHandle) = await ExecuteRequestLockAsync(_connection!, lockName, 0, releaseOnCommit: true, cancellationToken).ConfigureAwait(false);
            if (status == 0)
            {
                _logger.LogInformation("Successfully acquired Oracle transaction lock for resource '{ResourceId}'.", resourceId);
                DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "acquired");
                return Result<IDistributedLockHandle>.Success(new OracleAmbientLockHandle(resourceId));
            }

            DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "already_held");
            return DistributedLockErrors.LockAlreadyHeld;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "canceled");
            return DistributedLockErrors.Canceled;
        }
    }

    /// <inheritdoc />
    public async Task<Result<IDistributedLockHandle>> TryAcquireHandleAsync(
        string resourceId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        if (timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be non-negative or zero.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return DistributedLockErrors.Canceled;
        }

        var lockName = NormalizeLockName(resourceId);
        var timeoutSeconds = timeout.TotalSeconds > int.MaxValue
            ? int.MaxValue
            : (int)Math.Ceiling(timeout.TotalSeconds);

        if (_connectionFactory is not null)
        {
            var connection = _connectionFactory();
            try
            {
                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                }

                var (status, lockHandle) = await ExecuteRequestLockAsync(connection, lockName, timeoutSeconds, releaseOnCommit: false, cancellationToken).ConfigureAwait(false);
                if (status == 0)
                {
                    _logger.LogInformation("Successfully acquired Oracle session lock for resource '{ResourceId}' with timeout {Timeout}.", resourceId, timeout);
                    DistributedLockMetrics.RecordAcquisition(resourceId, "session", "acquired");
                    return Result<IDistributedLockHandle>.Success(
                        new OracleDistributedLockHandle(connection, lockHandle, resourceId, _logger, _options.KeepaliveCadence, _options.CommandTimeoutSeconds));
                }

                _logger.LogWarning("Failed to acquire Oracle lock for resource '{ResourceId}' within timeout {Timeout}. Status: {Status}.", resourceId, timeout, status);
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "timeout");
                await connection.DisposeAsync().ConfigureAwait(false);
                return DistributedLockErrors.Timeout;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "canceled");
                await connection.DisposeAsync().ConfigureAwait(false);
                return DistributedLockErrors.Canceled;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error acquiring Oracle lock with timeout for resource '{ResourceId}'.", resourceId);
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "error");
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        try
        {
            var (status, lockHandle) = await ExecuteRequestLockAsync(_connection!, lockName, timeoutSeconds, releaseOnCommit: true, cancellationToken).ConfigureAwait(false);
            if (status == 0)
            {
                _logger.LogInformation("Successfully acquired Oracle transaction lock for resource '{ResourceId}' with timeout {Timeout}.", resourceId, timeout);
                DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "acquired");
                return Result<IDistributedLockHandle>.Success(new OracleAmbientLockHandle(resourceId));
            }

            DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "timeout");
            return DistributedLockErrors.Timeout;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "canceled");
            return DistributedLockErrors.Canceled;
        }
    }

    /// <inheritdoc />
    public async Task<Result<IDistributedLockHandle>> AcquireHandleAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        var lockName = NormalizeLockName(resourceId);

        // In Oracle DBMS_LOCK, max timeout is 32767 seconds (~9.1 hours) or DBMS_LOCK.MAXWAIT (which is 32767)
        const int maxWaitSeconds = 32767;

        if (_connectionFactory is not null)
        {
            var connection = _connectionFactory();
            try
            {
                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                }

                var (status, lockHandle) = await ExecuteRequestLockAsync(connection, lockName, maxWaitSeconds, releaseOnCommit: false, cancellationToken).ConfigureAwait(false);
                if (status == 0)
                {
                    _logger.LogInformation("Successfully acquired blocking Oracle session lock for resource '{ResourceId}'.", resourceId);
                    DistributedLockMetrics.RecordAcquisition(resourceId, "session", "acquired");
                    return Result<IDistributedLockHandle>.Success(
                        new OracleDistributedLockHandle(connection, lockHandle, resourceId, _logger, _options.KeepaliveCadence, _options.CommandTimeoutSeconds));
                }

                _logger.LogWarning("Failed to acquire blocking Oracle lock for resource '{ResourceId}'. Status: {Status}.", resourceId, status);
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "already_held");
                await connection.DisposeAsync().ConfigureAwait(false);
                return DistributedLockErrors.LockAlreadyHeld;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "canceled");
                await connection.DisposeAsync().ConfigureAwait(false);
                return DistributedLockErrors.Canceled;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error acquiring blocking Oracle lock for resource '{ResourceId}'.", resourceId);
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "error");
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        try
        {
            var (status, lockHandle) = await ExecuteRequestLockAsync(_connection!, lockName, maxWaitSeconds, releaseOnCommit: true, cancellationToken).ConfigureAwait(false);
            if (status == 0)
            {
                _logger.LogInformation("Successfully acquired blocking Oracle transaction lock for resource '{ResourceId}'.", resourceId);
                DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "acquired");
                return Result<IDistributedLockHandle>.Success(new OracleAmbientLockHandle(resourceId));
            }

            DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "already_held");
            return DistributedLockErrors.LockAlreadyHeld;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "canceled");
            return DistributedLockErrors.Canceled;
        }
    }

    private async Task<(int Status, string LockHandle)> ExecuteRequestLockAsync(
        IDbConnection connection,
        string lockName,
        int timeoutSeconds,
        bool releaseOnCommit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var releaseOnCommitLiteral = releaseOnCommit ? "TRUE" : "FALSE";
        var sql = $"""
            DECLARE
                v_handle VARCHAR2(128);
                v_res INTEGER;
            BEGIN
                DBMS_LOCK.ALLOCATE_UNIQUE(:LockName, v_handle);
                :OutHandle := v_handle;
                v_res := DBMS_LOCK.REQUEST(v_handle, 6, :Timeout, {releaseOnCommitLiteral});
                :OutStatus := v_res;
            END;
            """;

        if (connection is DbConnection dbConnection)
        {
            await using var cmd = dbConnection.CreateCommand();
            cmd.CommandText = sql;
            cmd.CommandTimeout = _options.CommandTimeoutSeconds;

            var pName = cmd.CreateParameter();
            pName.ParameterName = "LockName";
            pName.Value = lockName;
            cmd.Parameters.Add(pName);

            var pTimeout = cmd.CreateParameter();
            pTimeout.ParameterName = "Timeout";
            pTimeout.Value = timeoutSeconds;
            cmd.Parameters.Add(pTimeout);

            var pOutHandle = cmd.CreateParameter();
            pOutHandle.ParameterName = "OutHandle";
            pOutHandle.Direction = ParameterDirection.Output;
            pOutHandle.DbType = DbType.String;
            pOutHandle.Size = 128;
            cmd.Parameters.Add(pOutHandle);

            var pOutStatus = cmd.CreateParameter();
            pOutStatus.ParameterName = "OutStatus";
            pOutStatus.Direction = ParameterDirection.Output;
            pOutStatus.DbType = DbType.Int32;
            cmd.Parameters.Add(pOutStatus);

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            var status = pOutStatus.Value is int s ? s : Convert.ToInt32(pOutStatus.Value, CultureInfo.InvariantCulture);
            var handle = pOutHandle.Value?.ToString() ?? string.Empty;
            return (status, handle);
        }

        using var syncCmd = connection.CreateCommand();
        syncCmd.CommandText = sql;
        syncCmd.CommandTimeout = _options.CommandTimeoutSeconds;

        var syncPName = syncCmd.CreateParameter();
        syncPName.ParameterName = "LockName";
        syncPName.Value = lockName;
        syncCmd.Parameters.Add(syncPName);

        var syncPTimeout = syncCmd.CreateParameter();
        syncPTimeout.ParameterName = "Timeout";
        syncPTimeout.Value = timeoutSeconds;
        syncCmd.Parameters.Add(syncPTimeout);

        var syncPOutHandle = syncCmd.CreateParameter();
        syncPOutHandle.ParameterName = "OutHandle";
        syncPOutHandle.Direction = ParameterDirection.Output;
        syncPOutHandle.DbType = DbType.String;
        syncPOutHandle.Size = 128;
        syncCmd.Parameters.Add(syncPOutHandle);

        var syncPOutStatus = syncCmd.CreateParameter();
        syncPOutStatus.ParameterName = "OutStatus";
        syncPOutStatus.Direction = ParameterDirection.Output;
        syncPOutStatus.DbType = DbType.Int32;
        syncCmd.Parameters.Add(syncPOutStatus);

        syncCmd.ExecuteNonQuery();

        var syncStatus = syncPOutStatus.Value is int ss ? ss : Convert.ToInt32(syncPOutStatus.Value, CultureInfo.InvariantCulture);
        var syncHandle = syncPOutHandle.Value?.ToString() ?? string.Empty;
        return (syncStatus, syncHandle);
    }

    private static bool IsAllHex64(string s)
    {
        if (s.Length != 64) return false;
        foreach (var c in s)
        {
            if (!char.IsAsciiHexDigit(c)) return false;
        }
        return true;
    }

    private static string NormalizeLockName(string resourceId)
    {
        if (resourceId.Length <= 128 && !IsAllHex64(resourceId))
        {
            return resourceId;
        }

        Span<byte> utf8Bytes = stackalloc byte[256];
        byte[]? rented = null;
        int maxBytes = Encoding.UTF8.GetMaxByteCount(resourceId.Length);
        Span<byte> buffer = maxBytes <= 256 ? utf8Bytes : (rented = ArrayPool<byte>.Shared.Rent(maxBytes));
        try
        {
            int count = Encoding.UTF8.GetBytes(resourceId.AsSpan(), buffer);
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(buffer[..count], hash);
            return Convert.ToHexString(hash);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    private sealed class OracleAmbientLockHandle : IDistributedLockHandle
    {
        public OracleAmbientLockHandle(string resourceId)
        {
            ResourceId = resourceId;

            Span<byte> utf8Bytes = stackalloc byte[256];
            byte[]? rented = null;
            int maxBytes = Encoding.UTF8.GetMaxByteCount(resourceId.Length);
            Span<byte> buffer = maxBytes <= 256 ? utf8Bytes : (rented = ArrayPool<byte>.Shared.Rent(maxBytes));
            try
            {
                int count = Encoding.UTF8.GetBytes(resourceId.AsSpan(), buffer);
                Span<byte> hash = stackalloc byte[32];
                SHA256.HashData(buffer[..count], hash);
                LockId = BinaryPrimitives.ReadInt64LittleEndian(hash);
            }
            finally
            {
                if (rented is not null)
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }
        }

        public string ResourceId { get; }
        public long LockId { get; }
        public CancellationToken HandleLostToken => CancellationToken.None;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
