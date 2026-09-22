// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.Result;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EricksonLopez.DistributedLock.PostgreSql;

/// <summary>
/// Provides distributed locks backed by PostgreSQL advisory locking functions.
/// Supports both transaction-bound locks and session-level locks with deterministic handle lifecycle.
/// </summary>
/// <remarks>
/// <para>
/// <b>PgBouncer / Connection Pooler Incompatibility:</b> Session-level advisory locks (<c>pg_advisory_lock</c>)
/// are bound directly to the backend PostgreSQL server process (<c>backend_pid</c>). When using connection poolers like
/// PgBouncer in <c>pool_mode = transaction</c> or <c>pool_mode = statement</c>, subsequent queries are multiplexed across
/// different physical server processes, which can cause session locks to leak or become inaccessible from the original client connection.
/// </para>
/// <para>
/// When operating behind PgBouncer with transaction-level pooling, always use transaction-scoped locks
/// (<see cref="PostgresTransactionLockExtensions.TryAcquireInTransactionAsync(System.Data.IDbTransaction, string, Microsoft.Extensions.Logging.ILogger, System.Threading.CancellationToken)"/>)
/// or connect to a dedicated pool configured with <c>pool_mode = session</c>.
/// </para>
/// </remarks>
public sealed class PostgresDistributedLockProvider : IDistributedLockProvider
{
    private readonly IDbConnection? _connection;
    private readonly Func<DbConnection>? _connectionFactory;
    private readonly ILogger<PostgresDistributedLockProvider> _logger;
    private readonly PostgresLockOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresDistributedLockProvider"/> class using an existing database connection for transaction-bound locks.
    /// </summary>
    /// <param name="connection">The database connection used for ambient transaction locks.</param>
    /// <param name="logger">The logger used for recording diagnostic and lifecycle events.</param>
    /// <param name="options">The configuration options for lock acquisition and keepalive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/> or <paramref name="logger"/> is <see langword="null"/></exception>
    public PostgresDistributedLockProvider(
        IDbConnection connection,
        ILogger<PostgresDistributedLockProvider> logger,
        PostgresLockOptions? options = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new PostgresLockOptions();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresDistributedLockProvider"/> class using a connection factory for dedicated sessions.
    /// </summary>
    /// <param name="connectionFactory">The delegate producing dedicated database connections per lock handle.</param>
    /// <param name="logger">The logger used for recording diagnostic and lifecycle events.</param>
    /// <param name="options">The configuration options for lock acquisition and keepalive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> or <paramref name="logger"/> is <see langword="null"/></exception>
    public PostgresDistributedLockProvider(
        Func<DbConnection> connectionFactory,
        ILogger<PostgresDistributedLockProvider> logger,
        PostgresLockOptions? options = null)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new PostgresLockOptions();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresDistributedLockProvider"/> class configured via options snapshot.
    /// </summary>
    /// <param name="connectionFactory">The delegate producing dedicated database connections per lock handle.</param>
    /// <param name="logger">The logger used for recording diagnostic and lifecycle events.</param>
    /// <param name="options">The configuration options accessor.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionFactory"/> or <paramref name="logger"/> is <see langword="null"/></exception>
    public PostgresDistributedLockProvider(
        Func<DbConnection> connectionFactory,
        ILogger<PostgresDistributedLockProvider> logger,
        IOptions<PostgresLockOptions> options)
        : this(connectionFactory, logger, options?.Value)
    {
    }

    /// <inheritdoc />
    public async Task<Result<IAsyncDisposable>> TryAcquireAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        var lockId = GenerateLockId(resourceId);

        if (_connectionFactory is not null)
        {
            var connection = _connectionFactory();
            try
            {
                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                }

                var acquired = await ExecuteTryAdvisoryLockAsync(connection, "SELECT pg_try_advisory_lock(@LockId);", lockId, cancellationToken).ConfigureAwait(false);

                if (acquired)
                {
                    _logger.LogInformation("Successfully acquired session-level advisory lock for resource '{ResourceId}'.", resourceId);
                    DistributedLockMetrics.RecordAcquisition(resourceId, "session", "acquired");
                    return Result<IAsyncDisposable>.Success(
                        new PostgresAdvisoryLockHandle(connection, lockId, resourceId, _logger, _options.KeepaliveCadence, _options.CommandTimeoutSeconds));
                }

                _logger.LogWarning("Failed to acquire session-level advisory lock for resource '{ResourceId}'. It is currently held by another process.", resourceId);
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
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "error");
                _logger.LogError(ex, "Unexpected error acquiring session-level advisory lock for resource '{ResourceId}'.", resourceId);
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        _logger.LogDebug("Attempting to acquire transaction-bound advisory lock for resource '{ResourceId}' with lock ID {LockId}.", resourceId, lockId);

        try
        {
            var xactAcquired = await ExecuteTryAdvisoryLockAsync(_connection!, "SELECT pg_try_advisory_xact_lock(@LockId);", lockId, cancellationToken).ConfigureAwait(false);

            if (xactAcquired)
            {
                _logger.LogInformation("Successfully acquired transaction-bound advisory lock for resource '{ResourceId}'.", resourceId);
                DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "acquired");
                return Result<IAsyncDisposable>.Success(new NoOpAsyncDisposable(resourceId, lockId));
            }

            _logger.LogWarning("Failed to acquire transaction-bound advisory lock for resource '{ResourceId}'. It is currently held by another process.", resourceId);
            DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "already_held");
            return DistributedLockErrors.LockAlreadyHeld;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "canceled");
            return DistributedLockErrors.Canceled;
        }
        catch (Exception ex)
        {
            DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "error");
            _logger.LogError(ex, "Unexpected error acquiring transaction-bound advisory lock for resource '{ResourceId}'.", resourceId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Result<IAsyncDisposable>> TryAcquireAsync(
        string resourceId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        if (timeout <= TimeSpan.Zero)
        {
            return await TryAcquireAsync(resourceId, cancellationToken).ConfigureAwait(false);
        }

        var lockId = GenerateLockId(resourceId);
        var stopwatch = Stopwatch.StartNew();
        var currentIntervalMs = _options.InitialPollingInterval.TotalMilliseconds;

        if (_connectionFactory is not null)
        {
            var connection = _connectionFactory();
            try
            {
                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                }

                _logger.LogDebug("Attempting to acquire session-level advisory lock for resource '{ResourceId}' with a timeout of {Timeout} ms.", resourceId, timeout.TotalMilliseconds);

                while (stopwatch.Elapsed < timeout)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        await connection.DisposeAsync().ConfigureAwait(false);
                        DistributedLockMetrics.RecordAcquisition(resourceId, "session", "canceled");
                        return DistributedLockErrors.Canceled;
                    }

                    var acquired = await ExecuteTryAdvisoryLockAsync(connection, "SELECT pg_try_advisory_lock(@LockId);", lockId, cancellationToken).ConfigureAwait(false);

                    if (acquired)
                    {
                        stopwatch.Stop();
                        _logger.LogInformation("Successfully acquired session-level advisory lock for resource '{ResourceId}' after polling ({ElapsedMs} ms).", resourceId, stopwatch.ElapsedMilliseconds);
                        DistributedLockMetrics.RecordAcquisition(resourceId, "session", "acquired");
                        DistributedLockMetrics.RecordWaitDuration(resourceId, "session", "acquired", stopwatch.Elapsed.TotalMilliseconds);
                        return Result<IAsyncDisposable>.Success(
                            new PostgresAdvisoryLockHandle(connection, lockId, resourceId, _logger, _options.KeepaliveCadence, _options.CommandTimeoutSeconds));
                    }

                    var remaining = timeout - stopwatch.Elapsed;
                    if (remaining <= TimeSpan.Zero)
                    {
                        break;
                    }

                    var delay = CalculateJitteredDelay(currentIntervalMs, _options.JitterRatio, remaining);
                    currentIntervalMs = Math.Min(currentIntervalMs * 1.5, _options.MaxPollingInterval.TotalMilliseconds);

                    try
                    {
                        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        await connection.DisposeAsync().ConfigureAwait(false);
                        DistributedLockMetrics.RecordAcquisition(resourceId, "session", "canceled");
                        return DistributedLockErrors.Canceled;
                    }
                }

                stopwatch.Stop();
                _logger.LogWarning("Timeout exceeded ({ElapsedMs} ms) while trying to acquire session-level advisory lock for resource '{ResourceId}'.", stopwatch.ElapsedMilliseconds, resourceId);
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "timeout");
                DistributedLockMetrics.RecordWaitDuration(resourceId, "session", "timeout", stopwatch.Elapsed.TotalMilliseconds);
                await connection.DisposeAsync().ConfigureAwait(false);
                return DistributedLockErrors.Timeout;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "canceled");
                return DistributedLockErrors.Canceled;
            }
            catch (Exception ex)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                _logger.LogError(ex, "Error during polling acquisition of session-level advisory lock for resource '{ResourceId}'.", resourceId);
                throw;
            }
        }

        _logger.LogDebug("Attempting to acquire transaction-bound advisory lock for resource '{ResourceId}' with a timeout of {Timeout} ms.", resourceId, timeout.TotalMilliseconds);

        try
        {
            while (stopwatch.Elapsed < timeout)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "canceled");
                    return DistributedLockErrors.Canceled;
                }

                var acquired = await ExecuteTryAdvisoryLockAsync(_connection!, "SELECT pg_try_advisory_xact_lock(@LockId);", lockId, cancellationToken).ConfigureAwait(false);

                if (acquired)
                {
                    stopwatch.Stop();
                    _logger.LogInformation("Successfully acquired transaction-bound advisory lock for resource '{ResourceId}' after polling ({ElapsedMs} ms).", resourceId, stopwatch.ElapsedMilliseconds);
                    DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "acquired");
                    DistributedLockMetrics.RecordWaitDuration(resourceId, "transaction", "acquired", stopwatch.Elapsed.TotalMilliseconds);
                    return Result<IAsyncDisposable>.Success(new NoOpAsyncDisposable(resourceId, lockId));
                }

                var remaining = timeout - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    break;
                }

                var delay = CalculateJitteredDelay(currentIntervalMs, _options.JitterRatio, remaining);
                currentIntervalMs = Math.Min(currentIntervalMs * 1.5, _options.MaxPollingInterval.TotalMilliseconds);

                try
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "canceled");
                    return DistributedLockErrors.Canceled;
                }
            }

            stopwatch.Stop();
            _logger.LogWarning("Timeout exceeded ({ElapsedMs} ms) while trying to acquire transaction-bound advisory lock for resource '{ResourceId}'.", stopwatch.ElapsedMilliseconds, resourceId);
            DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "timeout");
            DistributedLockMetrics.RecordWaitDuration(resourceId, "transaction", "timeout", stopwatch.Elapsed.TotalMilliseconds);
            return DistributedLockErrors.Timeout;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "canceled");
            return DistributedLockErrors.Canceled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during polling acquisition of transaction-bound advisory lock for resource '{ResourceId}'.", resourceId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<Result<IAsyncDisposable>> AcquireAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        var lockId = GenerateLockId(resourceId);
        var stopwatch = Stopwatch.StartNew();

        if (_connectionFactory is not null)
        {
            var connection = _connectionFactory();
            try
            {
                if (connection.State != ConnectionState.Open)
                {
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                }

                _logger.LogDebug("Blocking until session-level advisory lock is acquired for resource '{ResourceId}'.", resourceId);

                await ExecuteBlockingAdvisoryLockAsync(connection, "SELECT pg_advisory_lock(@LockId);", lockId, cancellationToken).ConfigureAwait(false);

                stopwatch.Stop();
                _logger.LogInformation("Successfully acquired blocking session-level advisory lock for resource '{ResourceId}' (Wait: {ElapsedMs} ms).", resourceId, stopwatch.ElapsedMilliseconds);
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "acquired");
                DistributedLockMetrics.RecordWaitDuration(resourceId, "session", "acquired", stopwatch.Elapsed.TotalMilliseconds);

                return Result<IAsyncDisposable>.Success(
                    new PostgresAdvisoryLockHandle(connection, lockId, resourceId, _logger, _options.KeepaliveCadence, _options.CommandTimeoutSeconds));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "canceled");
                return DistributedLockErrors.Canceled;
            }
            catch (Exception ex)
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    DistributedLockMetrics.RecordAcquisition(resourceId, "session", "canceled");
                    return DistributedLockErrors.Canceled;
                }

                _logger.LogError(ex, "Failed while blocking for session-level advisory lock on resource '{ResourceId}'.", resourceId);
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "error");
                throw;
            }
        }

        _logger.LogDebug("Blocking until transaction-bound advisory lock is acquired for resource '{ResourceId}'.", resourceId);

        try
        {
            await ExecuteBlockingAdvisoryLockAsync(_connection!, "SELECT pg_advisory_xact_lock(@LockId);", lockId, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            _logger.LogInformation("Successfully acquired blocking transaction-bound advisory lock for resource '{ResourceId}' (Wait: {ElapsedMs} ms).", resourceId, stopwatch.ElapsedMilliseconds);
            DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "acquired");
            DistributedLockMetrics.RecordWaitDuration(resourceId, "transaction", "acquired", stopwatch.Elapsed.TotalMilliseconds);

            return Result<IAsyncDisposable>.Success(new NoOpAsyncDisposable(resourceId, lockId));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "canceled");
            return DistributedLockErrors.Canceled;
        }
        catch (Exception ex)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "canceled");
                return DistributedLockErrors.Canceled;
            }

            _logger.LogError(ex, "Failed while blocking for transaction-bound advisory lock on resource '{ResourceId}'.", resourceId);
            DistributedLockMetrics.RecordAcquisition(resourceId, "transaction", "error");
            throw;
        }
    }

    /// <summary>
    /// Acquires a distributed lock for the specified resource, blocking until granted or timeout expires.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Branching behavior of this PostgreSQL-specific override:</b>
    /// </para>
    /// <list type="bullet">
    /// <item>
    ///   <description>
    ///     When <paramref name="timeout"/> is <see cref="Timeout.InfiniteTimeSpan"/>: delegates directly to
    ///     <see cref="AcquireAsync(string, CancellationToken)"/>, which issues <c>SELECT pg_advisory_lock(@LockId);</c>
    ///     — a native server-side blocking call that suspends until the lock is granted.
    ///   </description>
    /// </item>
    /// <item>
    ///   <description>
    ///     When <paramref name="timeout"/> is finite: delegates to <see cref="TryAcquireAsync(string, TimeSpan, CancellationToken)"/>,
    ///     which uses jittered exponential polling (<see cref="PostgresLockOptions.JitterRatio"/>) until the
    ///     deadline elapses or the lock is granted.
    ///   </description>
    /// </item>
    /// </list>
    /// </remarks>
    /// <param name="resourceId">The unique identifier of the resource to lock.</param>
    /// <param name="timeout">The maximum duration to wait for the lock. Pass <see cref="Timeout.InfiniteTimeSpan"/> for native blocking.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a <see cref="Result{T}"/>
    /// with an <see cref="IAsyncDisposable"/> handle if acquired; otherwise, the acquisition error.
    /// </returns>
    public async Task<Result<IAsyncDisposable>> AcquireAsync(
        string resourceId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout == Timeout.InfiniteTimeSpan)
        {
            return await AcquireAsync(resourceId, cancellationToken).ConfigureAwait(false);
        }

        // For bounded timeouts, utilize jittered polling to guarantee deterministic timeout enforcement across all PostgreSQL pooling modes
        return await TryAcquireAsync(resourceId, timeout, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="IDistributedLockProvider.TryAcquireHandleAsync(string, CancellationToken)" />
    public async Task<Result<IDistributedLockHandle>> TryAcquireHandleAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var result = await TryAcquireAsync(resourceId, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return result.Error;
        }

        if (result.Value is IDistributedLockHandle typedHandle)
        {
            return Result<IDistributedLockHandle>.Success(typedHandle);
        }

        // Defensive fallback: should not occur with current providers, but guards against future extension.
        return Result<IDistributedLockHandle>.Success(new NoOpAsyncDisposable(resourceId));
    }

    /// <inheritdoc cref="IDistributedLockProvider.TryAcquireHandleAsync(string, TimeSpan, CancellationToken)" />
    public async Task<Result<IDistributedLockHandle>> TryAcquireHandleAsync(
        string resourceId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var result = await TryAcquireAsync(resourceId, timeout, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return result.Error;
        }

        if (result.Value is IDistributedLockHandle typedHandle)
        {
            return Result<IDistributedLockHandle>.Success(typedHandle);
        }

        // Defensive fallback: should not occur with current providers, but guards against future extension.
        return Result<IDistributedLockHandle>.Success(new NoOpAsyncDisposable(resourceId));
    }

    /// <inheritdoc cref="IDistributedLockProvider.AcquireHandleAsync(string, CancellationToken)" />
    public async Task<Result<IDistributedLockHandle>> AcquireHandleAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        var result = await AcquireAsync(resourceId, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return result.Error;
        }

        if (result.Value is IDistributedLockHandle typedHandle)
        {
            return Result<IDistributedLockHandle>.Success(typedHandle);
        }

        // Defensive fallback: should not occur with current providers, but guards against future extension.
        return Result<IDistributedLockHandle>.Success(new NoOpAsyncDisposable(resourceId));
    }

    /// <inheritdoc cref="IDistributedLockProvider.AcquireHandleAsync(string, TimeSpan, CancellationToken)" />
    public async Task<Result<IDistributedLockHandle>> AcquireHandleAsync(
        string resourceId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var result = await AcquireAsync(resourceId, timeout, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return result.Error;
        }

        if (result.Value is IDistributedLockHandle typedHandle)
        {
            return Result<IDistributedLockHandle>.Success(typedHandle);
        }

        // Defensive fallback: should not occur with current providers, but guards against future extension.
        return Result<IDistributedLockHandle>.Success(new NoOpAsyncDisposable(resourceId));
    }

    /// <summary>
    /// Generates a deterministic 64-bit integer identifier from a string resource key.
    /// </summary>
    /// <remarks>
    /// Uses SHA-256 to ensure uniform distribution across the entire signed 64-bit space.
    /// The first 8 bytes of the SHA-256 digest are read as a signed 64-bit integer using
    /// <b>little-endian byte order</b> (<see cref="System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian"/>).
    /// This byte order is significant for interoperability: any external system computing the same lock identifier
    /// must apply the same endianness convention.
    /// </remarks>
    /// <param name="resourceId">The unique identifier of the resource to convert.</param>
    /// <returns>A signed 64-bit integer suitable for PostgreSQL advisory lock functions.</returns>
    /// <exception cref="ArgumentException"><paramref name="resourceId"/> is empty or contains only white space</exception>
    public static long GenerateLockId(string resourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        Span<byte> utf8Bytes = stackalloc byte[256];
        byte[]? rented = null;
        int maxBytes = Encoding.UTF8.GetMaxByteCount(resourceId.Length);
        Span<byte> buffer = maxBytes <= 256 ? utf8Bytes : (rented = System.Buffers.ArrayPool<byte>.Shared.Rent(maxBytes));
        try
        {
            int count = Encoding.UTF8.GetBytes(resourceId.AsSpan(), buffer);
            Span<byte> hash = stackalloc byte[32];
            SHA256.HashData(buffer[..count], hash);
            return System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(hash);
        }
        finally
        {
            if (rented is not null)
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    private static TimeSpan CalculateJitteredDelay(double baseIntervalMs, double jitterRatio, TimeSpan remainingTimeout)
    {
        var clampedRatio = Math.Clamp(jitterRatio, 0.0, 1.0);
        var jitterFactor = 1.0 + (Random.Shared.NextDouble() * 2.0 - 1.0) * clampedRatio;
        var calculatedMs = Math.Max(1.0, baseIntervalMs * jitterFactor);
        var boundedMs = Math.Min(calculatedMs, remainingTimeout.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(boundedMs);
    }

    private async Task<bool> ExecuteTryAdvisoryLockAsync(
        IDbConnection connection,
        string sql,
        long lockId,
        CancellationToken cancellationToken)
    {
        if (connection is DbConnection dbConnection)
        {
            await using var cmd = dbConnection.CreateCommand();
            cmd.CommandText = sql;
            var param = cmd.CreateParameter();
            param.ParameterName = "@LockId";
            param.Value = lockId;
            cmd.Parameters.Add(param);

            if (_options.CommandTimeoutSeconds.HasValue)
            {
                cmd.CommandTimeout = _options.CommandTimeoutSeconds.Value;
            }

            var scalar = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return scalar is true || (scalar is bool b && b);
        }

        using var syncCmd = connection.CreateCommand();
        syncCmd.CommandText = sql;
        var syncParam = syncCmd.CreateParameter();
        syncParam.ParameterName = "@LockId";
        syncParam.Value = lockId;
        syncCmd.Parameters.Add(syncParam);

        if (_options.CommandTimeoutSeconds.HasValue)
        {
            syncCmd.CommandTimeout = _options.CommandTimeoutSeconds.Value;
        }

        var result = syncCmd.ExecuteScalar();
        return result is true || (result is bool syncB && syncB);
    }

    private async Task ExecuteBlockingAdvisoryLockAsync(
        IDbConnection connection,
        string sql,
        long lockId,
        CancellationToken cancellationToken)
    {
        if (connection is DbConnection dbConnection)
        {
            await using var cmd = dbConnection.CreateCommand();
            cmd.CommandText = sql;
            var param = cmd.CreateParameter();
            param.ParameterName = "@LockId";
            param.Value = lockId;
            cmd.Parameters.Add(param);

            if (_options.CommandTimeoutSeconds.HasValue)
            {
                cmd.CommandTimeout = _options.CommandTimeoutSeconds.Value;
            }

            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        using var syncCmd = connection.CreateCommand();
        syncCmd.CommandText = sql;
        var syncParam = syncCmd.CreateParameter();
        syncParam.ParameterName = "@LockId";
        syncParam.Value = lockId;
        syncCmd.Parameters.Add(syncParam);

        if (_options.CommandTimeoutSeconds.HasValue)
        {
            syncCmd.CommandTimeout = _options.CommandTimeoutSeconds.Value;
        }

        syncCmd.ExecuteNonQuery();
    }
}
