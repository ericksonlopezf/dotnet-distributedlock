// Copyright © Erickson Lopez. MIT License.
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Data;
using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using Microsoft.Extensions.Logging;

namespace EricksonLopez.DistributedLock.Sqlite;

/// <summary>
/// Represents an acquired distributed lock backed by a SQLite database table.
/// </summary>
/// <remarks>
/// Releases the acquired SQLite lock record and closes the underlying database connection upon disposal.
/// </remarks>
public sealed class SqliteDistributedLockHandle : IDistributedLockHandle
{
    private readonly DbConnection _connection;
    private readonly string _resourceId;
    private readonly long _lockId;
    private readonly string _ownerId;
    private readonly ILogger _logger;
    private readonly int _commandTimeoutSeconds;
    private readonly long? _fencingToken;
    private readonly TimeSpan _lockTtl;
    private readonly CancellationTokenSource _handleLostCts = new();
    private readonly CancellationTokenSource _disposalCts = new();
    private readonly Task? _keepaliveTask;
    private int _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteDistributedLockHandle"/> class.
    /// </summary>
    /// <param name="connection">The database connection dedicated to holding the lock.</param>
    /// <param name="resourceId">The unique identifier of the resource associated with the lock.</param>
    /// <param name="ownerId">The unique identifier representing the lock owner.</param>
    /// <param name="logger">The logger used to record diagnostic and telemetry information.</param>
    /// <param name="commandTimeoutSeconds">The command timeout in seconds for lock queries.</param>
    /// <param name="fencingToken">An optional monotonic fencing token associated with the lock.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/>, <paramref name="resourceId"/>, <paramref name="ownerId"/>, or <paramref name="logger"/> is <see langword="null"/></exception>
    public SqliteDistributedLockHandle(
        DbConnection connection,
        string resourceId,
        string ownerId,
        ILogger logger,
        int commandTimeoutSeconds = 30,
        long? fencingToken = null)
        : this(connection, resourceId, ownerId, logger, TimeSpan.FromSeconds(60), null, commandTimeoutSeconds, fencingToken)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SqliteDistributedLockHandle"/> class with lease TTL and keepalive cadence.
    /// </summary>
    /// <param name="connection">The database connection dedicated to holding the lock.</param>
    /// <param name="resourceId">The unique identifier of the resource associated with the lock.</param>
    /// <param name="ownerId">The unique identifier representing the lock owner.</param>
    /// <param name="logger">The logger used to record diagnostic and telemetry information.</param>
    /// <param name="lockTtl">The lease duration before the lock expires.</param>
    /// <param name="keepaliveCadence">The cadence for renewing the lock lease, or <see langword="null"/> to disable keepalive renewal.</param>
    /// <param name="commandTimeoutSeconds">The command timeout in seconds for lock queries.</param>
    /// <param name="fencingToken">An optional monotonic fencing token associated with the lock.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/>, <paramref name="resourceId"/>, <paramref name="ownerId"/>, or <paramref name="logger"/> is <see langword="null"/></exception>
    public SqliteDistributedLockHandle(
        DbConnection connection,
        string resourceId,
        string ownerId,
        ILogger logger,
        TimeSpan lockTtl,
        TimeSpan? keepaliveCadence = null,
        int commandTimeoutSeconds = 30,
        long? fencingToken = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _resourceId = resourceId ?? throw new ArgumentNullException(nameof(resourceId));
        _ownerId = ownerId ?? throw new ArgumentNullException(nameof(ownerId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _commandTimeoutSeconds = commandTimeoutSeconds;
        _fencingToken = fencingToken;
        _lockTtl = lockTtl;

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

        if (keepaliveCadence.HasValue && keepaliveCadence.Value > TimeSpan.Zero)
        {
            _keepaliveTask = Task.Run(() => RunRenewalLoopAsync(keepaliveCadence.Value, _disposalCts.Token));
        }
    }

    /// <inheritdoc />
    public string ResourceId => _resourceId;

    /// <inheritdoc />
    public long LockId => _lockId;

    /// <inheritdoc />
    public CancellationToken HandleLostToken => _handleLostCts.Token;

    /// <inheritdoc />
    public long? FencingToken => _fencingToken;

    private async Task RunRenewalLoopAsync(TimeSpan cadence, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(cadence);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (Interlocked.CompareExchange(ref _isDisposed, 0, 0) != 0)
                {
                    break;
                }

                if (_connection.State != ConnectionState.Open)
                {
                    _logger.LogWarning("SQLite lock connection is no longer open for resource '{ResourceId}'. Triggering HandleLostToken.", _resourceId);
                    SafeCancelHandleLost();
                    DistributedLockMetrics.RecordLockLost(_resourceId, _lockId);
                    break;
                }

                try
                {
                    var ttlSeconds = Math.Max(1, (int)_lockTtl.TotalSeconds);
                    await using var cmd = _connection.CreateCommand();
                    cmd.CommandText = "UPDATE __distributed_locks SET expires_at = datetime('now', '+' || @TtlSeconds || ' seconds') WHERE resource_id = @ResourceId AND owner_id = @OwnerId;";
                    cmd.CommandTimeout = _commandTimeoutSeconds;

                    var pTtl = cmd.CreateParameter();
                    pTtl.ParameterName = "@TtlSeconds";
                    pTtl.Value = ttlSeconds;
                    cmd.Parameters.Add(pTtl);

                    var pRes = cmd.CreateParameter();
                    pRes.ParameterName = "@ResourceId";
                    pRes.Value = _resourceId;
                    cmd.Parameters.Add(pRes);

                    var pOwner = cmd.CreateParameter();
                    pOwner.ParameterName = "@OwnerId";
                    pOwner.Value = _ownerId;
                    cmd.Parameters.Add(pOwner);

                    var updated = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    if (updated == 0)
                    {
                        _logger.LogWarning("SQLite lock renewal updated 0 rows for resource '{ResourceId}'. Lock was lost or preempted. Triggering HandleLostToken.", _resourceId);
                        SafeCancelHandleLost();
                        DistributedLockMetrics.RecordLockLost(_resourceId, _lockId);
                        break;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "SQLite lock renewal query failed for resource '{ResourceId}'. Triggering HandleLostToken.", _resourceId);
                    SafeCancelHandleLost();
                    DistributedLockMetrics.RecordLockLost(_resourceId, _lockId);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Clean disposal
        }
    }

    private void SafeCancelHandleLost()
    {
        if (Interlocked.CompareExchange(ref _isDisposed, 0, 0) != 0)
        {
            return;
        }

        try
        {
            _handleLostCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // CTS already disposed during teardown
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        try
        {
            _disposalCts.Cancel();

            if (_keepaliveTask is not null)
            {
                try
                {
                    await _keepaliveTask.ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Ignore keepalive cancellation exceptions during disposal
                }
            }
        }
        finally
        {
            _disposalCts.Dispose();
            _handleLostCts.Dispose();

            try
            {
                if (_connection.State == ConnectionState.Open)
                {
                    await using var command = _connection.CreateCommand();
                    command.CommandText = "DELETE FROM __distributed_locks WHERE resource_id = @ResourceId AND owner_id = @OwnerId;";
                    command.CommandTimeout = _commandTimeoutSeconds;

                    var pRes = command.CreateParameter();
                    pRes.ParameterName = "@ResourceId";
                    pRes.Value = _resourceId;
                    command.Parameters.Add(pRes);

                    var pOwner = command.CreateParameter();
                    pOwner.ParameterName = "@OwnerId";
                    pOwner.Value = _ownerId;
                    command.Parameters.Add(pOwner);

                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                    _logger.LogInformation("Released SQLite lock for resource '{ResourceId}'.", _resourceId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error releasing SQLite lock for resource '{ResourceId}'.", _resourceId);
            }
            finally
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
