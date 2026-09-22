// Copyright © Erickson Lopez. MIT License.
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace EricksonLopez.DistributedLock.Redis;

/// <summary>
/// Represents an acquired distributed lock backed by Redis.
/// </summary>
/// <remarks>
/// Releases the acquired Redis lock when disposed. An optional keepalive background loop
/// can periodically renew the lock lease until disposal.
/// </remarks>
public sealed class RedisDistributedLockHandle : IDistributedLockHandle
{
    private const string ReleaseLua =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

    private const string RenewLua =
        "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('pexpire', KEYS[1], ARGV[2]) else return 0 end";

    private readonly IDatabase _database;
    private readonly RedisKey _key;
    private readonly RedisValue _lockValue;
    private readonly string _resourceId;
    private readonly long _lockId;
    private readonly TimeSpan _expiry;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _handleLostCts = new();
    private readonly CancellationTokenSource _disposalCts = new();
    private readonly Task? _keepaliveTask;
    private int _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisDistributedLockHandle"/> class.
    /// </summary>
    /// <param name="database">The Redis database instance used to manage the lock.</param>
    /// <param name="key">The Redis key representing the locked resource.</param>
    /// <param name="lockValue">The unique value identifying the lock ownership token.</param>
    /// <param name="resourceId">The unique identifier of the resource associated with the lock.</param>
    /// <param name="expiry">The lease duration for the lock.</param>
    /// <param name="logger">The logger used to record diagnostic and telemetry information.</param>
    /// <param name="keepaliveCadence">The cadence for renewing the lock lease, or <see langword="null"/> to disable keepalive renewal.</param>
    /// <exception cref="ArgumentNullException"><paramref name="database"/>, <paramref name="resourceId"/>, or <paramref name="logger"/> is <see langword="null"/></exception>
    public RedisDistributedLockHandle(
        IDatabase database,
        RedisKey key,
        RedisValue lockValue,
        string resourceId,
        TimeSpan expiry,
        ILogger logger,
        TimeSpan? keepaliveCadence = null)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _key = key;
        _lockValue = lockValue;
        _resourceId = resourceId ?? throw new ArgumentNullException(nameof(resourceId));
        _expiry = expiry;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

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

                try
                {
                    var evalResult = await _database.ScriptEvaluateAsync(
                        RenewLua,
                        [_key],
                        [_lockValue, (long)_expiry.TotalMilliseconds])
                        .ConfigureAwait(false);

                    var result = (int)evalResult;
                    if (result == 0)
                    {
                        _logger.LogWarning("Redis lock lease renewal failed for resource '{ResourceId}'. Lock was lost.", _resourceId);
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
                    _logger.LogWarning(ex, "Redis lock renewal error for resource '{ResourceId}'. Triggering HandleLostToken.", _resourceId);
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
                await _database.ScriptEvaluateAsync(ReleaseLua, [_key], [_lockValue]).ConfigureAwait(false);
                _logger.LogInformation("Released Redis lock for resource '{ResourceId}'.", _resourceId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error releasing Redis lock for resource '{ResourceId}'.", _resourceId);
            }
        }
    }
}
