// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.Result;

namespace EricksonLopez.DistributedLock.Showcase.Infrastructure;

/// <summary>
/// A high-performance in-memory reference implementation of <see cref="IDistributedLockProvider"/>.
/// Demonstrates custom provider implementation with monotonically increasing fencing tokens.
/// </summary>
public sealed class InMemoryDistributedLockProvider : IDistributedLockProvider
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Semaphores = new(StringComparer.Ordinal);
    private static long _globalFencingSequence;

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

    public async Task<Result<IDistributedLockHandle>> TryAcquireHandleAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        var sem = Semaphores.GetOrAdd(resourceId, _ => new SemaphoreSlim(1, 1));
        bool entered;
        try
        {
            entered = await sem.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            DistributedLockMetrics.RecordAcquisition(resourceId, "session", "canceled");
            return DistributedLockErrors.Canceled;
        }

        if (!entered)
        {
            DistributedLockMetrics.RecordAcquisition(resourceId, "session", "already_held");
            return DistributedLockErrors.LockAlreadyHeld;
        }

        var fencingToken = Interlocked.Increment(ref _globalFencingSequence);
        DistributedLockMetrics.RecordAcquisition(resourceId, "session", "acquired");
        return Result<IDistributedLockHandle>.Success(new InMemoryLockHandle(resourceId, sem, fencingToken));
    }

    public async Task<Result<IDistributedLockHandle>> TryAcquireHandleAsync(
        string resourceId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        var sem = Semaphores.GetOrAdd(resourceId, _ => new SemaphoreSlim(1, 1));
        bool entered;
        try
        {
            entered = await sem.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            DistributedLockMetrics.RecordAcquisition(resourceId, "session", "canceled");
            return DistributedLockErrors.Canceled;
        }

        if (!entered)
        {
            DistributedLockMetrics.RecordAcquisition(resourceId, "session", "timeout");
            return DistributedLockErrors.Timeout;
        }

        var fencingToken = Interlocked.Increment(ref _globalFencingSequence);
        DistributedLockMetrics.RecordAcquisition(resourceId, "session", "acquired");
        return Result<IDistributedLockHandle>.Success(new InMemoryLockHandle(resourceId, sem, fencingToken));
    }

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

    public async Task<Result<IDistributedLockHandle>> AcquireHandleAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        return await TryAcquireHandleAsync(resourceId, Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<IAsyncDisposable>> AcquireAsync(
        string resourceId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var handleResult = await AcquireHandleAsync(resourceId, timeout, cancellationToken).ConfigureAwait(false);
        if (handleResult.IsFailure)
        {
            return Result<IAsyncDisposable>.Failure(handleResult.Error);
        }

        return Result<IAsyncDisposable>.Success(handleResult.Value);
    }

    public async Task<Result<IDistributedLockHandle>> AcquireHandleAsync(
        string resourceId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return await TryAcquireHandleAsync(resourceId, timeout, cancellationToken).ConfigureAwait(false);
    }

    public sealed class InMemoryLockHandle : IDistributedLockHandle
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly CancellationTokenSource _lostCts = new();
        private int _isDisposed;

        public InMemoryLockHandle(string resourceId, SemaphoreSlim semaphore, long fencingToken)
        {
            ResourceId = resourceId;
            _semaphore = semaphore;
            FencingToken = fencingToken;
            LockId = HashCode.Combine(resourceId, fencingToken);
        }

        public string ResourceId { get; }
        public long LockId { get; }
        public CancellationToken HandleLostToken => _lostCts.Token;
        public long? FencingToken { get; }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
            {
                _semaphore.Release();
                _lostCts.Dispose();
            }

            return ValueTask.CompletedTask;
        }

        public void SimulateLockLoss()
        {
            if (!_lostCts.IsCancellationRequested)
            {
                _lostCts.Cancel();
            }
        }
    }
}
