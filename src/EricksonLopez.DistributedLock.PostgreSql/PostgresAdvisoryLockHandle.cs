// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using Microsoft.Extensions.Logging;

namespace EricksonLopez.DistributedLock.PostgreSql;

/// <summary>
/// Represents a session-level PostgreSQL advisory lock handle that maintains heartbeats and releases the lock upon disposal.
/// </summary>
/// <seealso cref="IDistributedLockHandle"/>
/// <remarks>
/// <para>
/// <b>Implementation note</b>: Although this class is <see langword="public"/>, it is an implementation
/// detail of <see cref="PostgresDistributedLockProvider"/>. Consumers should interact with this handle
/// exclusively through the <see cref="IDistributedLockHandle"/> interface. Direct instantiation by
/// consumer code is not a supported use case.
/// </para>
/// </remarks>
public sealed class PostgresAdvisoryLockHandle : IDistributedLockHandle
{
    private readonly DbConnection _connection;
    private readonly long _lockId;
    private readonly string _resourceId;
    private readonly ILogger _logger;
    private readonly int? _commandTimeoutSeconds;
    private readonly CancellationTokenSource _handleLostCts = new();
    private readonly CancellationTokenSource _disposalCts = new();
    private readonly Stopwatch _holdStopwatch = Stopwatch.StartNew();
    private readonly Task? _keepaliveTask;
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresAdvisoryLockHandle"/> class with the specified connection and lock parameters.
    /// </summary>
    /// <param name="connection">The dedicated database connection holding the session lock.</param>
    /// <param name="lockId">The 64-bit numerical identifier of the advisory lock.</param>
    /// <param name="resourceId">The logical resource identifier requested by the caller.</param>
    /// <param name="logger">The logger used for recording diagnostic and lifecycle events.</param>
    /// <param name="keepaliveCadence">The interval for periodic heartbeat queries to detect connection termination.</param>
    /// <param name="commandTimeoutSeconds">The command timeout in seconds for lock maintenance commands.</param>
    /// <exception cref="ArgumentNullException"><paramref name="connection"/>, <paramref name="resourceId"/>, or <paramref name="logger"/> is <see langword="null"/></exception>
    public PostgresAdvisoryLockHandle(
        DbConnection connection,
        long lockId,
        string resourceId,
        ILogger logger,
        TimeSpan keepaliveCadence = default,
        int? commandTimeoutSeconds = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _lockId = lockId;
        _resourceId = resourceId ?? throw new ArgumentNullException(nameof(resourceId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _commandTimeoutSeconds = commandTimeoutSeconds;

        if (keepaliveCadence > TimeSpan.Zero)
        {
            _keepaliveTask = Task.Run(() => RunKeepaliveLoopAsync(keepaliveCadence, _disposalCts.Token));
        }
    }

    /// <inheritdoc />
    public CancellationToken HandleLostToken => _handleLostCts.Token;

    /// <inheritdoc />
    public string ResourceId => _resourceId;

    /// <inheritdoc />
    public long LockId => _lockId;

    private async Task RunKeepaliveLoopAsync(TimeSpan cadence, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(cadence);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (_connection.State != ConnectionState.Open)
                {
                    _logger.LogWarning(
                        "Distributed lock connection is no longer open for resource '{ResourceId}' (Lock ID: {LockId}). State: {State}",
                        _resourceId, _lockId, _connection.State);
                    SafeCancelHandleLost();
                    DistributedLockMetrics.RecordLockLost(_resourceId, _lockId);
                    break;
                }

                try
                {
                    await using var cmd = _connection.CreateCommand();
                    cmd.CommandText = "SELECT 1;";
                    if (_commandTimeoutSeconds.HasValue)
                    {
                        cmd.CommandTimeout = _commandTimeoutSeconds.Value;
                    }

                    await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Keepalive heartbeat ping failed for session-level lock on resource '{ResourceId}' (Lock ID: {LockId}). Signaling lock lost.",
                        _resourceId, _lockId);
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
        if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0)
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
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
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

            if (_connection.State == ConnectionState.Open)
            {
                try
                {
                    await using var cmd = _connection.CreateCommand();
                    cmd.CommandText = "SELECT pg_advisory_unlock(@LockId);";
                    var param = cmd.CreateParameter();
                    param.ParameterName = "@LockId";
                    param.Value = _lockId;
                    cmd.Parameters.Add(param);

                    if (_commandTimeoutSeconds.HasValue)
                    {
                        cmd.CommandTimeout = _commandTimeoutSeconds.Value;
                    }

                    await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);

                    _logger.LogInformation(
                        "Released session-level distributed lock for resource '{ResourceId}' (Lock ID: {LockId}).",
                        _resourceId, _lockId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Error releasing distributed lock for resource '{ResourceId}' (Lock ID: {LockId}).",
                        _resourceId, _lockId);
                }
            }
        }
        finally
        {
            _holdStopwatch.Stop();
            DistributedLockMetrics.RecordHoldDuration(_resourceId, "session", _holdStopwatch.Elapsed.TotalMilliseconds);

            _disposalCts.Dispose();
            _handleLostCts.Dispose();
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
