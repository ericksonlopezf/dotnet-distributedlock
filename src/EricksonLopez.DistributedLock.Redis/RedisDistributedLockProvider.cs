// Copyright © Erickson Lopez. MIT License.
using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.Result;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace EricksonLopez.DistributedLock.Redis;

/// <summary>
/// Provides distributed locking capabilities backed by Redis key-value storage.
/// </summary>
/// <remarks>
/// <para>
/// <b>Redis Lease Expiration &amp; GC Pause Warning:</b> Redis distributed locks rely on TTL expiration (<c>SET NX PX</c>).
/// If an application process experiences a prolonged garbage collection pause (STW GC) or network partition exceeding
/// the lock lease duration (<see cref="RedisLockOptions.DefaultExpiry"/>), Redis will automatically expire the key,
/// allowing another node to claim ownership while the original worker is still processing.
/// </para>
/// <para>
/// To mitigate this risk, configure <see cref="RedisLockOptions.KeepaliveCadence"/> for automated background renewal,
/// keep critical section operations brief, and link background processing to <see cref="IDistributedLockHandle.HandleLostToken"/>.
/// </para>
/// </remarks>
public sealed class RedisDistributedLockProvider : IDistributedLockProvider
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly ILogger<RedisDistributedLockProvider> _logger;
    private readonly RedisLockOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisDistributedLockProvider"/> class.
    /// </summary>
    /// <param name="multiplexer">The connection multiplexer used to communicate with Redis.</param>
    /// <param name="logger">The logger used to record diagnostic and telemetry information.</param>
    /// <param name="options">The configuration options for this provider, or <see langword="null"/> to use default options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="multiplexer"/> or <paramref name="logger"/> is <see langword="null"/></exception>
    public RedisDistributedLockProvider(
        IConnectionMultiplexer multiplexer,
        ILogger<RedisDistributedLockProvider> logger,
        RedisLockOptions? options = null)
    {
        _multiplexer = multiplexer ?? throw new ArgumentNullException(nameof(multiplexer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? new RedisLockOptions();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RedisDistributedLockProvider"/> class configured via <see cref="IOptions{TOptions}"/>.
    /// </summary>
    /// <param name="multiplexer">The connection multiplexer used to communicate with Redis.</param>
    /// <param name="logger">The logger used to record diagnostic and telemetry information.</param>
    /// <param name="options">The options accessor containing provider configuration options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="multiplexer"/> or <paramref name="logger"/> is <see langword="null"/></exception>
    public RedisDistributedLockProvider(
        IConnectionMultiplexer multiplexer,
        ILogger<RedisDistributedLockProvider> logger,
        IOptions<RedisLockOptions> options)
        : this(multiplexer, logger, options?.Value)
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

        var db = _multiplexer.GetDatabase();
        var key = (RedisKey)(_options.KeyPrefix + resourceId);
        var token = (RedisValue)Guid.NewGuid().ToString("N");

        try
        {
            var acquired = await db.StringSetAsync(key, token, _options.DefaultExpiry, When.NotExists).ConfigureAwait(false);
            if (acquired)
            {
                _logger.LogInformation("Successfully acquired Redis lock for resource '{ResourceId}'.", resourceId);
                DistributedLockMetrics.RecordAcquisition(resourceId, "session", "acquired");
                return Result<IDistributedLockHandle>.Success(
                    new RedisDistributedLockHandle(db, key, token, resourceId, _options.DefaultExpiry, _logger, _options.KeepaliveCadence));
            }

            _logger.LogWarning("Failed to acquire Redis lock for resource '{ResourceId}'. Currently held.", resourceId);
            DistributedLockMetrics.RecordAcquisition(resourceId, "session", "already_held");
            return DistributedLockErrors.LockAlreadyHeld;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DistributedLockMetrics.RecordAcquisition(resourceId, "session", "canceled");
            return DistributedLockErrors.Canceled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error acquiring Redis lock for resource '{ResourceId}'.", resourceId);
            DistributedLockMetrics.RecordAcquisition(resourceId, "session", "error");
            throw;
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
}
