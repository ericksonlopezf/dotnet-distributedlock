// Copyright © Erickson Lopez. MIT License.
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.Result;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EricksonLopez.DistributedLock.Sqlite;

/// <summary>
/// Provides distributed locking capabilities backed by a SQLite database lock table.
/// </summary>
public sealed class SqliteDistributedLockProvider : IDistributedLockProvider
{
    private readonly IDbConnection? _connection;
    private readonly Func<DbConnection>? _connectionFactory;
    private readonly ILogger<SqliteDistributedLockProvider> _logger;
    private readonly SqliteLockOptions _options;
    private bool _tableInitialized;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteDistributedLockProvider"/> class using an existing database connection.
    /// </summary>
    /// <param name="connection">The database connection used to manage lock records.</param>
    /// <param name="logger">The logger used to record diagnostic and telemetry information.</param>
    /// <param name="options">The configuration options for this provider, or <see langword="null"/> to use default options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> or <paramref name="logger"/> is <see langword="null"/></exception>
    public SqliteDistributedLockProvider(
        IDbConnection connection,
        ILogger<SqliteDistributedLockProvider> logger,
        SqliteLockOptions? options = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new SqliteLockOptions();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteDistributedLockProvider"/> class using a connection factory delegate.
    /// </summary>
    /// <param name="connectionFactory">The factory delegate that creates new database connections.</param>
    /// <param name="logger">The logger used to record diagnostic and telemetry information.</param>
    /// <param name="options">The configuration options for this provider, or <see langword="null"/> to use default options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> or <paramref name="logger"/> is <see langword="null"/></exception>
    public SqliteDistributedLockProvider(
        Func<DbConnection> connectionFactory,
        ILogger<SqliteDistributedLockProvider> logger,
        SqliteLockOptions? options = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new SqliteLockOptions();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteDistributedLockProvider"/> class configured via <see cref="IOptions{TOptions}"/>.
    /// </summary>
    /// <param name="connectionFactory">The factory delegate that creates new database connections.</param>
    /// <param name="logger">The logger used to record diagnostic and telemetry information.</param>
    /// <param name="options">The options accessor containing provider configuration options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> or <paramref name="logger"/> is <see langword="null"/></exception>
    public SqliteDistributedLockProvider(
        Func<DbConnection> connectionFactory,
        ILogger<SqliteDistributedLockProvider> logger,
        IOptions<SqliteLockOptions> options)
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

        var ownerId = Guid.NewGuid().ToString("N");

        if (_connectionFactory is not null)
        {
            var connection = _connectionFactory();
            try
            {
                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                }

                await EnsureTableCreatedAsync(connection, cancellationToken).ConfigureAwait(false);

                var (acquired, fencingToken) = await ExecuteTryInsertLockAsync(connection, resourceId, ownerId, cancellationToken).ConfigureAwait(false);
                if (acquired)
                {
                    _logger.LogInformation("Successfully acquired SQLite lock for resource '{ResourceId}'.", resourceId);
                    DistributedLockMetrics.RecordAcquisition(resourceId, "session", "acquired");
                    return Result<IDistributedLockHandle>.Success(
                        new SqliteDistributedLockHandle(
                            connection,
                            resourceId,
                            ownerId,
                            _logger,
                            _options.LockTtl,
                            _options.KeepaliveCadence,
                            _options.CommandTimeoutSeconds,
                            fencingToken));
                }

                _logger.LogWarning("Failed to acquire SQLite lock for resource '{ResourceId}'. Currently held.", resourceId);
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
                _logger.LogError(ex, "Unexpected error acquiring SQLite lock for resource '{ResourceId}'.", resourceId);
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "error");
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        try
        {
            await EnsureTableCreatedAsync((DbConnection)_connection!, cancellationToken).ConfigureAwait(false);
            var (acquired, fencingToken) = await ExecuteTryInsertLockAsync((DbConnection)_connection!, resourceId, ownerId, cancellationToken).ConfigureAwait(false);
            if (acquired)
            {
                _logger.LogInformation("Successfully acquired SQLite lock for resource '{ResourceId}'.", resourceId);
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "acquired");
                return Result<IDistributedLockHandle>.Success(
                    new SqliteAmbientLockHandle(
                        resourceId,
                        (DbConnection)_connection!,
                        ownerId,
                        _logger,
                        _options.CommandTimeoutSeconds,
                        fencingToken));
            }

            DistributedLockMetrics.RecordAcquisition(resourceId, "session", "already_held");
            return DistributedLockErrors.LockAlreadyHeld;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DistributedLockMetrics.RecordAcquisition(resourceId, "session", "canceled");
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

        var sw = Stopwatch.StartNew();
        var baseDelayMs = (int)_options.RetryInterval.TotalMilliseconds;
        if (baseDelayMs <= 0) baseDelayMs = 50;

        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "canceled");
                return DistributedLockErrors.Canceled;
            }

            var firstAttempt = await TryAcquireHandleAsync(resourceId, cancellationToken).ConfigureAwait(false);
            if (firstAttempt.IsSuccess)
            {
                return firstAttempt;
            }

            var elapsed = sw.Elapsed;
            if (elapsed >= timeout)
            {
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "timeout");
                return DistributedLockErrors.Timeout;
            }

            var remaining = timeout - elapsed;
            var delayMs = baseDelayMs;
            if (_options.BackoffJitter)
            {
                delayMs += RandomNumberGenerator.GetInt32(0, baseDelayMs);
            }

            if (delayMs > remaining.TotalMilliseconds)
            {
                delayMs = (int)remaining.TotalMilliseconds;
            }

            if (delayMs <= 0)
            {
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "timeout");
                return DistributedLockErrors.Timeout;
            }

            try
            {
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "canceled");
                return DistributedLockErrors.Canceled;
            }
        }
    }

    /// <inheritdoc />
    public async Task<Result<IDistributedLockHandle>> AcquireHandleAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        var baseDelayMs = (int)_options.RetryInterval.TotalMilliseconds;
        if (baseDelayMs <= 0) baseDelayMs = 50;

        while (true)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "canceled");
                return DistributedLockErrors.Canceled;
            }

            var attempt = await TryAcquireHandleAsync(resourceId, cancellationToken).ConfigureAwait(false);
            if (attempt.IsSuccess)
            {
                return attempt;
            }

            var delayMs = baseDelayMs;
            if (_options.BackoffJitter)
            {
                delayMs += RandomNumberGenerator.GetInt32(0, baseDelayMs);
            }

            try
            {
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "canceled");
                return DistributedLockErrors.Canceled;
            }
        }
    }

    private async Task EnsureTableCreatedAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        if (_tableInitialized) return;

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS __distributed_locks (
                resource_id TEXT PRIMARY KEY,
                owner_id TEXT NOT NULL,
                acquired_at TEXT NOT NULL,
                expires_at TEXT NOT NULL DEFAULT (datetime('now', '+60 seconds'))
            );
            """;
        cmd.CommandTimeout = _options.CommandTimeoutSeconds;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        _tableInitialized = true;
    }

    private async Task<(bool Acquired, long? FencingToken)> ExecuteTryInsertLockAsync(
        DbConnection connection,
        string resourceId,
        string ownerId,
        CancellationToken cancellationToken)
    {
        var ttlSeconds = Math.Max(1, (int)_options.LockTtl.TotalSeconds);

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO __distributed_locks (resource_id, owner_id, acquired_at, expires_at)
                VALUES (@ResourceId, @OwnerId, datetime('now'), datetime('now', '+' || @TtlSeconds || ' seconds'))
                ON CONFLICT(resource_id) DO UPDATE
                SET owner_id = excluded.owner_id,
                    acquired_at = excluded.acquired_at,
                    expires_at = excluded.expires_at
                WHERE __distributed_locks.expires_at <= datetime('now');
                """;
            cmd.CommandTimeout = _options.CommandTimeoutSeconds;

            var pRes = cmd.CreateParameter();
            pRes.ParameterName = "@ResourceId";
            pRes.Value = resourceId;
            cmd.Parameters.Add(pRes);

            var pOwner = cmd.CreateParameter();
            pOwner.ParameterName = "@OwnerId";
            pOwner.Value = ownerId;
            cmd.Parameters.Add(pOwner);

            var pTtl = cmd.CreateParameter();
            pTtl.ParameterName = "@TtlSeconds";
            pTtl.Value = ttlSeconds;
            cmd.Parameters.Add(pTtl);

            var rowsAffected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (rowsAffected > 0)
            {
                return (true, DateTime.UtcNow.Ticks);
            }

            return (false, null);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT
        {
            return (false, null);
        }
    }

    private sealed class SqliteAmbientLockHandle : IDistributedLockHandle
    {
        private readonly string _resourceId;
        private readonly DbConnection _connection;
        private readonly string _ownerId;
        private readonly ILogger _logger;
        private readonly int _commandTimeoutSeconds;
        private readonly long? _fencingToken;
        private readonly long _lockId;
        private int _isDisposed;

        public SqliteAmbientLockHandle(
            string resourceId,
            DbConnection connection,
            string ownerId,
            ILogger logger,
            int commandTimeoutSeconds = 30,
            long? fencingToken = null)
        {
            _resourceId = resourceId ?? throw new ArgumentNullException(nameof(resourceId));
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _ownerId = ownerId ?? throw new ArgumentNullException(nameof(ownerId));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _commandTimeoutSeconds = commandTimeoutSeconds;
            _fencingToken = fencingToken;

            Span<byte> utf8Bytes = stackalloc byte[256];
            byte[]? rented = null;
            int maxBytes = Encoding.UTF8.GetMaxByteCount(resourceId.Length);
            Span<byte> buffer = maxBytes <= 256 ? utf8Bytes : (rented = ArrayPool<byte>.Shared.Rent(maxBytes));
            try
            {
                int count = Encoding.UTF8.GetBytes(resourceId.AsSpan(), buffer);
                Span<byte> hash = stackalloc byte[32];
                SHA256.HashData(buffer[..count], hash);
                _lockId = BinaryPrimitives.ReadInt64LittleEndian(hash);
            }
            finally
            {
                if (rented is not null)
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }
        }

        public string ResourceId => _resourceId;
        public long LockId => _lockId;
        public CancellationToken HandleLostToken => CancellationToken.None;
        public long? FencingToken => _fencingToken;

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
            {
                return;
            }

            try
            {
                if (_connection.State == ConnectionState.Open)
                {
                    await using var cmd = _connection.CreateCommand();
                    cmd.CommandText = "DELETE FROM __distributed_locks WHERE resource_id = @ResourceId AND owner_id = @OwnerId;";
                    cmd.CommandTimeout = _commandTimeoutSeconds;
                    var pRes = cmd.CreateParameter();
                    pRes.ParameterName = "@ResourceId";
                    pRes.Value = _resourceId;
                    cmd.Parameters.Add(pRes);

                    var pOwner = cmd.CreateParameter();
                    pOwner.ParameterName = "@OwnerId";
                    pOwner.Value = _ownerId;
                    cmd.Parameters.Add(pOwner);

                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error releasing SQLite ambient lock for resource '{ResourceId}'.", _resourceId);
            }
        }
    }
}
