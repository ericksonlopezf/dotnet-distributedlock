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

namespace EricksonLopez.DistributedLock.MySql;

/// <summary>
/// Provides distributed locks backed by MySQL user-level locking functions.
/// </summary>
public sealed class MySqlDistributedLockProvider : IDistributedLockProvider
{
    private readonly IDbConnection? _connection;
    private readonly Func<DbConnection>? _connectionFactory;
    private readonly ILogger<MySqlDistributedLockProvider> _logger;
    private readonly MySqlLockOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlDistributedLockProvider"/> class using an existing database connection.
    /// </summary>
    /// <param name="connection">The database connection used for ambient session locks.</param>
    /// <param name="logger">The logger used for recording diagnostic and lifecycle events.</param>
    /// <param name="options">The configuration options for lock acquisition and keepalive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> or <paramref name="logger"/> is <see langword="null"/></exception>
    public MySqlDistributedLockProvider(
        IDbConnection connection,
        ILogger<MySqlDistributedLockProvider> logger,
        MySqlLockOptions? options = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new MySqlLockOptions();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlDistributedLockProvider"/> class using a connection factory for dedicated sessions.
    /// </summary>
    /// <param name="connectionFactory">The delegate producing dedicated database connections per lock handle.</param>
    /// <param name="logger">The logger used for recording diagnostic and lifecycle events.</param>
    /// <param name="options">The configuration options for lock acquisition and keepalive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> or <paramref name="logger"/> is <see langword="null"/></exception>
    public MySqlDistributedLockProvider(
        Func<DbConnection> connectionFactory,
        ILogger<MySqlDistributedLockProvider> logger,
        MySqlLockOptions? options = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new MySqlLockOptions();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MySqlDistributedLockProvider"/> class configured via options snapshot.
    /// </summary>
    /// <param name="connectionFactory">The delegate producing dedicated database connections per lock handle.</param>
    /// <param name="logger">The logger used for recording diagnostic and lifecycle events.</param>
    /// <param name="options">The configuration options accessor.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> or <paramref name="logger"/> is <see langword="null"/></exception>
    public MySqlDistributedLockProvider(
        Func<DbConnection> connectionFactory,
        ILogger<MySqlDistributedLockProvider> logger,
        IOptions<MySqlLockOptions> options)
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

        var lockResource = NormalizeResourceKey(resourceId);

        if (_connectionFactory is not null)
        {
            var connection = _connectionFactory();
            try
            {
                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                }

                var acquired = await ExecuteGetLockAsync(connection, lockResource, 0, cancellationToken).ConfigureAwait(false);
                if (acquired)
                {
                    _logger.LogInformation("Successfully acquired MySQL lock for resource '{ResourceId}'.", resourceId);
                    DistributedLockMetrics.RecordAcquisition(resourceId, "session", "acquired");
                    return Result<IDistributedLockHandle>.Success(
                        new MySqlDistributedLockHandle(connection, lockResource, resourceId, _logger, _options.KeepaliveCadence, _options.CommandTimeoutSeconds));
                }

                _logger.LogWarning("Failed to acquire MySQL lock for resource '{ResourceId}'. Currently held.", resourceId);
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
                _logger.LogError(ex, "Unexpected error acquiring MySQL lock for resource '{ResourceId}'.", resourceId);
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "error");
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        try
        {
            var acquired = await ExecuteGetLockAsync(_connection!, lockResource, 0, cancellationToken).ConfigureAwait(false);
            if (acquired)
            {
                _logger.LogInformation("Successfully acquired MySQL lock for resource '{ResourceId}'.", resourceId);
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "acquired");
                return Result<IDistributedLockHandle>.Success(new MySqlAmbientLockHandle(_connection!, resourceId, lockResource, _logger, _options.CommandTimeoutSeconds));
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

        var lockResource = NormalizeResourceKey(resourceId);
        var timeoutSeconds = checked((int)Math.Ceiling(timeout.TotalSeconds));

        if (_connectionFactory is not null)
        {
            var connection = _connectionFactory();
            try
            {
                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                }

                var acquired = await ExecuteGetLockAsync(connection, lockResource, timeoutSeconds, cancellationToken).ConfigureAwait(false);
                if (acquired)
                {
                    _logger.LogInformation("Successfully acquired MySQL lock for resource '{ResourceId}' with timeout {Timeout}.", resourceId, timeout);
                    DistributedLockMetrics.RecordAcquisition(resourceId, "session", "acquired");
                    return Result<IDistributedLockHandle>.Success(
                        new MySqlDistributedLockHandle(connection, lockResource, resourceId, _logger, _options.KeepaliveCadence, _options.CommandTimeoutSeconds));
                }

                _logger.LogWarning("Failed to acquire MySQL lock for resource '{ResourceId}' within timeout {Timeout}.", resourceId, timeout);
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
                _logger.LogError(ex, "Unexpected error acquiring MySQL lock with timeout for resource '{ResourceId}'.", resourceId);
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "error");
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        try
        {
            var acquired = await ExecuteGetLockAsync(_connection!, lockResource, timeoutSeconds, cancellationToken).ConfigureAwait(false);
            if (acquired)
            {
                _logger.LogInformation("Successfully acquired MySQL lock for resource '{ResourceId}' with timeout {Timeout}.", resourceId, timeout);
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "acquired");
                return Result<IDistributedLockHandle>.Success(new MySqlAmbientLockHandle(_connection!, resourceId, lockResource, _logger, _options.CommandTimeoutSeconds));
            }

            DistributedLockMetrics.RecordAcquisition(resourceId, "session", "timeout");
            return DistributedLockErrors.Timeout;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DistributedLockMetrics.RecordAcquisition(resourceId, "session", "canceled");
            return DistributedLockErrors.Canceled;
        }
    }

    /// <inheritdoc />
    public async Task<Result<IDistributedLockHandle>> AcquireHandleAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        var lockResource = NormalizeResourceKey(resourceId);

        // In MySQL GET_LOCK, passing a very large timeout (e.g. 0x7FFFFFFF) simulates blocking indefinitely
        const int infiniteTimeout = int.MaxValue;

        if (_connectionFactory is not null)
        {
            var connection = _connectionFactory();
            try
            {
                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                }

                var acquired = await ExecuteGetLockAsync(connection, lockResource, infiniteTimeout, cancellationToken).ConfigureAwait(false);
                if (acquired)
                {
                    _logger.LogInformation("Successfully acquired blocking MySQL lock for resource '{ResourceId}'.", resourceId);
                    DistributedLockMetrics.RecordAcquisition(resourceId, "session", "acquired");
                    return Result<IDistributedLockHandle>.Success(
                        new MySqlDistributedLockHandle(connection, lockResource, resourceId, _logger, _options.KeepaliveCadence, _options.CommandTimeoutSeconds));
                }

                _logger.LogWarning("Failed to acquire blocking MySQL lock for resource '{ResourceId}'.", resourceId);
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
                _logger.LogError(ex, "Unexpected error acquiring blocking MySQL lock for resource '{ResourceId}'.", resourceId);
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "error");
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        try
        {
            var acquired = await ExecuteGetLockAsync(_connection!, lockResource, infiniteTimeout, cancellationToken).ConfigureAwait(false);
            if (acquired)
            {
                _logger.LogInformation("Successfully acquired blocking MySQL lock for resource '{ResourceId}'.", resourceId);
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "acquired");
                return Result<IDistributedLockHandle>.Success(new MySqlAmbientLockHandle(_connection!, resourceId, lockResource, _logger, _options.CommandTimeoutSeconds));
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

    private async Task<bool> ExecuteGetLockAsync(
        IDbConnection connection,
        string lockResource,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (connection is DbConnection dbConnection)
        {
            await using var command = dbConnection.CreateCommand();
            command.CommandText = "SELECT GET_LOCK(@Resource, @Timeout);";
            command.CommandTimeout = _options.CommandTimeoutSeconds;

            var pResource = command.CreateParameter();
            pResource.ParameterName = "@Resource";
            pResource.Value = lockResource;
            command.Parameters.Add(pResource);

            var pTimeout = command.CreateParameter();
            pTimeout.ParameterName = "@Timeout";
            pTimeout.Value = timeoutSeconds;
            command.Parameters.Add(pTimeout);

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            if (result is null || Convert.IsDBNull(result))
            {
                return false;
            }

            return Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1;
        }

        using var syncCommand = connection.CreateCommand();
        syncCommand.CommandText = "SELECT GET_LOCK(@Resource, @Timeout);";
        syncCommand.CommandTimeout = _options.CommandTimeoutSeconds;

        var syncPResource = syncCommand.CreateParameter();
        syncPResource.ParameterName = "@Resource";
        syncPResource.Value = lockResource;
        syncCommand.Parameters.Add(syncPResource);

        var syncPTimeout = syncCommand.CreateParameter();
        syncPTimeout.ParameterName = "@Timeout";
        syncPTimeout.Value = timeoutSeconds;
        syncCommand.Parameters.Add(syncPTimeout);

        var syncResult = syncCommand.ExecuteScalar();
        if (syncResult is null || Convert.IsDBNull(syncResult))
        {
            return false;
        }

        return Convert.ToInt32(syncResult, CultureInfo.InvariantCulture) == 1;
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

    private static string NormalizeResourceKey(string resourceId)
    {
        if (resourceId.Length <= 64 && !IsAllHex64(resourceId))
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

    private sealed class MySqlAmbientLockHandle : IDistributedLockHandle
    {
        private readonly IDbConnection _connection;
        private readonly string _lockResource;
        private readonly ILogger _logger;
        private readonly int _commandTimeoutSeconds;
        private int _isDisposed;

        public MySqlAmbientLockHandle(
            IDbConnection connection,
            string resourceId,
            string lockResource,
            ILogger logger,
            int commandTimeoutSeconds = 30)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            ResourceId = resourceId ?? throw new ArgumentNullException(nameof(resourceId));
            _lockResource = lockResource;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _commandTimeoutSeconds = commandTimeoutSeconds;

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
                    if (_connection is DbConnection dbConn)
                    {
                        await using var command = dbConn.CreateCommand();
                        command.CommandText = "SELECT RELEASE_LOCK(@Resource);";
                        command.CommandTimeout = _commandTimeoutSeconds;

                        var p = command.CreateParameter();
                        p.ParameterName = "@Resource";
                        p.Value = _lockResource;
                        command.Parameters.Add(p);

                        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        using var command = _connection.CreateCommand();
                        command.CommandText = "SELECT RELEASE_LOCK(@Resource);";
                        command.CommandTimeout = _commandTimeoutSeconds;

                        var p = command.CreateParameter();
                        p.ParameterName = "@Resource";
                        p.Value = _lockResource;
                        command.Parameters.Add(p);

                        command.ExecuteNonQuery();
                    }

                    _logger.LogInformation("Released MySQL ambient lock for resource '{ResourceId}'.", ResourceId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error releasing MySQL ambient lock for resource '{ResourceId}'.", ResourceId);
            }
        }
    }
}
