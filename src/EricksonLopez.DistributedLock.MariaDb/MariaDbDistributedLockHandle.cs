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

namespace EricksonLopez.DistributedLock.MariaDb;

/// <summary>
/// Represents an active MariaDB user-level lock handle that releases the lock upon disposal.
/// </summary>
public sealed class MariaDbDistributedLockHandle : IDistributedLockHandle
{
    private readonly DbConnection _connection;
    private readonly string _lockResource;
    private readonly string _resourceId;
    private readonly long _lockId;
    private readonly ILogger _logger;
    private readonly int _commandTimeoutSeconds;
    private readonly CancellationTokenSource _handleLostCts = new();
    private readonly CancellationTokenSource _disposalCts = new();
    private readonly Task? _keepaliveTask;
    private int _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="MariaDbDistributedLockHandle"/> class with the specified connection and lock parameters.
    /// </summary>
    /// <param name="connection">The underlying database connection holding the user-level lock.</param>
    /// <param name="lockResource">The database-level lock resource identifier.</param>
    /// <param name="resourceId">The logical resource identifier requested by the caller.</param>
    /// <param name="logger">The logger used for recording diagnostic and lifecycle events.</param>
    /// <param name="keepaliveCadence">The optional interval for heartbeat queries to detect dropped connections.</param>
    /// <param name="commandTimeoutSeconds">The command timeout in seconds for lock maintenance commands.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/>, <paramref name="lockResource"/>, <paramref name="resourceId"/>, or <paramref name="logger"/> is <see langword="null"/></exception>
    public MariaDbDistributedLockHandle(
        DbConnection connection,
        string lockResource,
        string resourceId,
        ILogger logger,
        TimeSpan? keepaliveCadence = null,
        int commandTimeoutSeconds = 30)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _lockResource = lockResource ?? throw new ArgumentNullException(nameof(lockResource));
        _resourceId = resourceId ?? throw new ArgumentNullException(nameof(resourceId));
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
            _keepaliveTask = Task.Run(() => RunKeepaliveLoopAsync(keepaliveCadence.Value, _disposalCts.Token));
        }
    }

    /// <inheritdoc />
    public string ResourceId => _resourceId;

    /// <inheritdoc />
    public long LockId => _lockId;

    /// <inheritdoc />
    public CancellationToken HandleLostToken => _handleLostCts.Token;

    private async Task RunKeepaliveLoopAsync(TimeSpan cadence, CancellationToken cancellationToken)
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
                    _logger.LogWarning("MariaDB lock connection is no longer open for resource '{ResourceId}'. Triggering HandleLostToken.", _resourceId);
                    SafeCancelHandleLost();
                    DistributedLockMetrics.RecordLockLost(_resourceId, _lockId);
                    break;
                }

                try
                {
                    await using var cmd = _connection.CreateCommand();
                    cmd.CommandText = "SELECT 1;";
                    cmd.CommandTimeout = _commandTimeoutSeconds;
                    await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "MariaDB lock keepalive failed for resource '{ResourceId}'. Triggering HandleLostToken.", _resourceId);
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

    private void ExecuteKeepalive(object? state)
    {
        if (Interlocked.CompareExchange(ref _isDisposed, 0, 0) != 0)
        {
            return;
        }

        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT 1;";
            cmd.CommandTimeout = _commandTimeoutSeconds;
            cmd.ExecuteScalar();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MariaDB lock keepalive failed for resource '{ResourceId}'. Triggering HandleLostToken.", _resourceId);
            SafeCancelHandleLost();
            DistributedLockMetrics.RecordLockLost(_resourceId, _lockId);
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
                    command.CommandText = "SELECT RELEASE_LOCK(@Resource);";
                    command.CommandTimeout = _commandTimeoutSeconds;

                    var p = command.CreateParameter();
                    p.ParameterName = "@Resource";
                    p.Value = _lockResource;
                    command.Parameters.Add(p);

                    await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                    _logger.LogInformation("Released MariaDB lock for resource '{ResourceId}'.", _resourceId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error releasing MariaDB lock for resource '{ResourceId}'. The connection will be closed.", _resourceId);
            }
            finally
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
