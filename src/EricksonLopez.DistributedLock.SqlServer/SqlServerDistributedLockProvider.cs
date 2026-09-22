// Copyright © Erickson Lopez. MIT License.
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.Result;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EricksonLopez.DistributedLock.SqlServer;

/// <summary>
/// Provides distributed locking capabilities backed by Microsoft SQL Server application locks.
/// </summary>
/// <remarks>
/// <para>
/// <b>Database Collation Sensitivity:</b> Resource key comparisons in SQL Server are collation-sensitive.
/// On case-insensitive databases (such as <c>SQL_Latin1_General_CP1_CI_AS</c>), resource keys differing only
/// by casing resolve to the same underlying application lock resource.
/// </para>
/// </remarks>
public sealed class SqlServerDistributedLockProvider : IDistributedLockProvider
{
    private readonly IDbConnection? _connection;
    private readonly Func<DbConnection>? _connectionFactory;
    private readonly ILogger<SqlServerDistributedLockProvider> _logger;
    private readonly SqlServerLockOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerDistributedLockProvider"/> class using an existing database connection.
    /// </summary>
    /// <param name="connection">The database connection used for acquiring application locks.</param>
    /// <param name="logger">The logger used to record diagnostic and telemetry information.</param>
    /// <param name="options">The configuration options for this provider, or <see langword="null"/> to use default options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> or <paramref name="logger"/> is <see langword="null"/></exception>
    public SqlServerDistributedLockProvider(
        IDbConnection connection,
        ILogger<SqlServerDistributedLockProvider> logger,
        SqlServerLockOptions? options = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new SqlServerLockOptions();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerDistributedLockProvider"/> class using a connection factory delegate.
    /// </summary>
    /// <param name="connectionFactory">The factory delegate that creates new database connections.</param>
    /// <param name="logger">The logger used to record diagnostic and telemetry information.</param>
    /// <param name="options">The configuration options for this provider, or <see langword="null"/> to use default options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> or <paramref name="logger"/> is <see langword="null"/></exception>
    public SqlServerDistributedLockProvider(
        Func<DbConnection> connectionFactory,
        ILogger<SqlServerDistributedLockProvider> logger,
        SqlServerLockOptions? options = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new SqlServerLockOptions();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqlServerDistributedLockProvider"/> class configured via <see cref="IOptions{TOptions}"/>.
    /// </summary>
    /// <param name="connectionFactory">The factory delegate that creates new database connections.</param>
    /// <param name="logger">The logger used to record diagnostic and telemetry information.</param>
    /// <param name="options">The options accessor containing provider configuration options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> or <paramref name="logger"/> is <see langword="null"/></exception>
    public SqlServerDistributedLockProvider(
        Func<DbConnection> connectionFactory,
        ILogger<SqlServerDistributedLockProvider> logger,
        IOptions<SqlServerLockOptions> options)
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

                var returnCode = await ExecuteGetAppLockAsync(connection, lockResource, "Session", 0, cancellationToken).ConfigureAwait(false);
                if (returnCode >= 0)
                {
                    _logger.LogInformation("Successfully acquired SQL Server session lock for resource '{ResourceId}'.", resourceId);
                    DistributedLockMetrics.RecordAcquisition(resourceId, "session", "acquired");
                    return Result<IDistributedLockHandle>.Success(
                        new SqlServerDistributedLockHandle(connection, lockResource, resourceId, "Session", _logger, _options.KeepaliveCadence, _options.CommandTimeoutSeconds));
                }

                _logger.LogWarning("Failed to acquire SQL Server session lock for resource '{ResourceId}'. Return code: {Code}.", resourceId, returnCode);
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
                _logger.LogError(ex, "Unexpected error acquiring SQL Server session lock for resource '{ResourceId}'.", resourceId);
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "error");
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        // Ambient / Transaction-bound lock
        try
        {
            var returnCode = await ExecuteGetAppLockAsync(_connection!, lockResource, "Transaction", 0, cancellationToken).ConfigureAwait(false);
            if (returnCode >= 0)
            {
                _logger.LogInformation("Successfully acquired SQL Server transaction lock for resource '{ResourceId}'.", resourceId);
                DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "acquired");
                return Result<IDistributedLockHandle>.Success(new SqlServerTransactionLockHandle(resourceId));
            }

            _logger.LogWarning("Failed to acquire SQL Server transaction lock for resource '{ResourceId}'. Return code: {Code}.", resourceId, returnCode);
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

        var lockResource = NormalizeResourceKey(resourceId);
        var timeoutMs = timeout.TotalMilliseconds > int.MaxValue
            ? int.MaxValue
            : (int)timeout.TotalMilliseconds;

        if (_connectionFactory is not null)
        {
            var connection = _connectionFactory();
            try
            {
                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                }

                var returnCode = await ExecuteGetAppLockAsync(connection, lockResource, "Session", timeoutMs, cancellationToken).ConfigureAwait(false);
                if (returnCode >= 0)
                {
                    _logger.LogInformation("Successfully acquired SQL Server session lock for resource '{ResourceId}' with timeout {Timeout}.", resourceId, timeout);
                    return Result<IDistributedLockHandle>.Success(
                        new SqlServerDistributedLockHandle(connection, lockResource, resourceId, "Session", _logger, _options.KeepaliveCadence, _options.CommandTimeoutSeconds));
                }

                _logger.LogWarning("Failed to acquire SQL Server session lock for resource '{ResourceId}' within timeout {Timeout}. Return code: {Code}.", resourceId, timeout, returnCode);
                await connection.DisposeAsync().ConfigureAwait(false);
                return DistributedLockErrors.Timeout;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                return DistributedLockErrors.Canceled;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error acquiring SQL Server session lock with timeout for resource '{ResourceId}'.", resourceId);
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        // Ambient / Transaction-bound lock
        try
        {
            var returnCode = await ExecuteGetAppLockAsync(_connection!, lockResource, "Transaction", timeoutMs, cancellationToken).ConfigureAwait(false);
            if (returnCode >= 0)
            {
                _logger.LogInformation("Successfully acquired SQL Server transaction lock for resource '{ResourceId}' with timeout {Timeout}.", resourceId, timeout);
                return Result<IDistributedLockHandle>.Success(new SqlServerTransactionLockHandle(resourceId));
            }

            _logger.LogWarning("Failed to acquire SQL Server transaction lock for resource '{ResourceId}' within timeout {Timeout}. Return code: {Code}.", resourceId, timeout, returnCode);
            return DistributedLockErrors.Timeout;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
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

        if (_connectionFactory is not null)
        {
            var connection = _connectionFactory();
            try
            {
                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                }

                // Timeout -1 = wait indefinitely
                var returnCode = await ExecuteGetAppLockAsync(connection, lockResource, "Session", -1, cancellationToken).ConfigureAwait(false);
                if (returnCode >= 0)
                {
                    _logger.LogInformation("Successfully acquired blocking SQL Server session lock for resource '{ResourceId}'.", resourceId);
                    return Result<IDistributedLockHandle>.Success(
                        new SqlServerDistributedLockHandle(connection, lockResource, resourceId, "Session", _logger, _options.KeepaliveCadence, _options.CommandTimeoutSeconds));
                }

                _logger.LogWarning("Failed to acquire blocking SQL Server session lock for resource '{ResourceId}'. Return code: {Code}.", resourceId, returnCode);
                await connection.DisposeAsync().ConfigureAwait(false);
                return DistributedLockErrors.LockAlreadyHeld;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                return DistributedLockErrors.Canceled;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error acquiring blocking SQL Server session lock for resource '{ResourceId}'.", resourceId);
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        // Ambient / Transaction-bound lock
        try
        {
            var returnCode = await ExecuteGetAppLockAsync(_connection!, lockResource, "Transaction", -1, cancellationToken).ConfigureAwait(false);
            if (returnCode >= 0)
            {
                _logger.LogInformation("Successfully acquired blocking SQL Server transaction lock for resource '{ResourceId}'.", resourceId);
                return Result<IDistributedLockHandle>.Success(new SqlServerTransactionLockHandle(resourceId));
            }

            _logger.LogWarning("Failed to acquire blocking SQL Server transaction lock for resource '{ResourceId}'. Return code: {Code}.", resourceId, returnCode);
            return DistributedLockErrors.LockAlreadyHeld;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return DistributedLockErrors.Canceled;
        }
    }

    private async Task<int> ExecuteGetAppLockAsync(
        IDbConnection connection,
        string lockResource,
        string lockOwner,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (connection is DbConnection dbConnection)
        {
            await using var command = dbConnection.CreateCommand();
            command.CommandText = """
                DECLARE @res INT;
                EXEC @res = sys.sp_getapplock 
                    @Resource = @Resource, 
                    @LockMode = 'Exclusive', 
                    @LockOwner = @LockOwner, 
                    @LockTimeout = @LockTimeout;
                SELECT @res;
                """;
            command.CommandTimeout = _options.CommandTimeoutSeconds;

            var pResource = command.CreateParameter();
            pResource.ParameterName = "@Resource";
            pResource.Value = lockResource;
            command.Parameters.Add(pResource);

            var pOwner = command.CreateParameter();
            pOwner.ParameterName = "@LockOwner";
            pOwner.Value = lockOwner;
            command.Parameters.Add(pOwner);

            var pTimeout = command.CreateParameter();
            pTimeout.ParameterName = "@LockTimeout";
            pTimeout.Value = timeoutMs;
            command.Parameters.Add(pTimeout);

            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return result is int code ? code : Convert.ToInt32(result, CultureInfo.InvariantCulture);
        }

        using var syncCommand = connection.CreateCommand();
        syncCommand.CommandText = """
            DECLARE @res INT;
            EXEC @res = sys.sp_getapplock 
                @Resource = @Resource, 
                @LockMode = 'Exclusive', 
                @LockOwner = @LockOwner, 
                @LockTimeout = @LockTimeout;
            SELECT @res;
            """;
        syncCommand.CommandTimeout = _options.CommandTimeoutSeconds;

        var syncPResource = syncCommand.CreateParameter();
        syncPResource.ParameterName = "@Resource";
        syncPResource.Value = lockResource;
        syncCommand.Parameters.Add(syncPResource);

        var syncPOwner = syncCommand.CreateParameter();
        syncPOwner.ParameterName = "@LockOwner";
        syncPOwner.Value = lockOwner;
        syncCommand.Parameters.Add(syncPOwner);

        var syncPTimeout = syncCommand.CreateParameter();
        syncPTimeout.ParameterName = "@LockTimeout";
        syncPTimeout.Value = timeoutMs;
        syncCommand.Parameters.Add(syncPTimeout);

        var syncResult = syncCommand.ExecuteScalar();
        return syncResult is int syncCode ? syncCode : Convert.ToInt32(syncResult, CultureInfo.InvariantCulture);
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
        if (resourceId.Length <= 255 && !IsAllHex64(resourceId))
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

    private sealed class SqlServerTransactionLockHandle : IDistributedLockHandle
    {
        public SqlServerTransactionLockHandle(string resourceId)
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

