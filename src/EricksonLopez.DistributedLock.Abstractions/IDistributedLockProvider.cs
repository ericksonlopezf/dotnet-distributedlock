// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Result;

namespace EricksonLopez.DistributedLock.Abstractions;

/// <summary>
/// Defines a contract for coordinating distributed mutual exclusion across process boundaries.
/// </summary>
public interface IDistributedLockProvider
{
    /// <summary>
    /// Attempts to acquire a distributed lock for the specified resource immediately without waiting.
    /// </summary>
    /// <param name="resourceId">The unique identifier of the resource to lock.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a <see cref="Result{T}"/>
    /// with an <see cref="IAsyncDisposable"/> handle if the lock was acquired;
    /// otherwise, a failure with <see cref="DistributedLockErrors.LockAlreadyHeld"/>.
    /// </returns>
    Task<Result<IAsyncDisposable>> TryAcquireAsync(
        string resourceId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to acquire a distributed lock for the specified resource within a maximum duration.
    /// </summary>
    /// <param name="resourceId">The unique identifier of the resource to lock.</param>
    /// <param name="timeout">The maximum duration to wait for the lock.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a <see cref="Result{T}"/>
    /// with an <see cref="IAsyncDisposable"/> handle if the lock was acquired;
    /// otherwise, a failure with <see cref="DistributedLockErrors.Timeout"/>.
    /// </returns>
    Task<Result<IAsyncDisposable>> TryAcquireAsync(
        string resourceId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Acquires a distributed lock for the specified resource, blocking until the lock is granted.
    /// </summary>
    /// <remarks>
    /// The default interface implementation delegates to <see cref="TryAcquireAsync(string, TimeSpan, CancellationToken)"/>
    /// with <see cref="Timeout.InfiniteTimeSpan"/>. Concrete implementations may override this method
    /// to use native engine blocking primitives.
    /// </remarks>
    /// <param name="resourceId">The unique identifier of the resource to lock.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a <see cref="Result{T}"/>
    /// with an <see cref="IAsyncDisposable"/> handle if acquired; otherwise, the acquisition error.
    /// </returns>
    Task<Result<IAsyncDisposable>> AcquireAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        return TryAcquireAsync(resourceId, Timeout.InfiniteTimeSpan, cancellationToken);
    }

    /// <summary>
    /// Acquires a distributed lock for the specified resource, blocking until granted or timeout expires.
    /// </summary>
    /// <remarks>
    /// The default interface implementation delegates to <see cref="TryAcquireAsync(string, TimeSpan, CancellationToken)"/>.
    /// Concrete implementations may override this method to use provider-specific bounded-wait mechanisms.
    /// </remarks>
    /// <param name="resourceId">The unique identifier of the resource to lock.</param>
    /// <param name="timeout">The maximum duration to wait for the lock.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a <see cref="Result{T}"/>
    /// with an <see cref="IAsyncDisposable"/> handle if acquired; otherwise, the acquisition error.
    /// </returns>
    Task<Result<IAsyncDisposable>> AcquireAsync(
        string resourceId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        return TryAcquireAsync(resourceId, timeout, cancellationToken);
    }

    /// <summary>
    /// Attempts to acquire a strongly typed distributed lock handle immediately without waiting.
    /// </summary>
    /// <param name="resourceId">The unique identifier of the resource to lock.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a <see cref="Result{T}"/>
    /// with an <see cref="IDistributedLockHandle"/> if acquired; otherwise, the acquisition error.
    /// </returns>
    async Task<Result<IDistributedLockHandle>> TryAcquireHandleAsync(
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

        return Result<IDistributedLockHandle>.Success(new AsyncDisposableHandleAdapter(result.Value, resourceId));
    }

    /// <summary>
    /// Attempts to acquire a strongly typed distributed lock handle within a maximum duration.
    /// </summary>
    /// <param name="resourceId">The unique identifier of the resource to lock.</param>
    /// <param name="timeout">The maximum duration to wait for the lock.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a <see cref="Result{T}"/>
    /// with an <see cref="IDistributedLockHandle"/> if acquired; otherwise, the acquisition error.
    /// </returns>
    async Task<Result<IDistributedLockHandle>> TryAcquireHandleAsync(
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

        return Result<IDistributedLockHandle>.Success(new AsyncDisposableHandleAdapter(result.Value, resourceId));
    }

    /// <summary>
    /// Acquires a strongly typed distributed lock handle, blocking until the lock is granted.
    /// </summary>
    /// <param name="resourceId">The unique identifier of the resource to lock.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a <see cref="Result{T}"/>
    /// with an <see cref="IDistributedLockHandle"/> if acquired; otherwise, the acquisition error.
    /// </returns>
    async Task<Result<IDistributedLockHandle>> AcquireHandleAsync(
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

        return Result<IDistributedLockHandle>.Success(new AsyncDisposableHandleAdapter(result.Value, resourceId));
    }

    /// <summary>
    /// Acquires a strongly typed distributed lock handle, blocking until granted or timeout expires.
    /// </summary>
    /// <param name="resourceId">The unique identifier of the resource to lock.</param>
    /// <param name="timeout">The maximum duration to wait for the lock.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a <see cref="Result{T}"/>
    /// with an <see cref="IDistributedLockHandle"/> if acquired; otherwise, the acquisition error.
    /// </returns>
    async Task<Result<IDistributedLockHandle>> AcquireHandleAsync(
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

        return Result<IDistributedLockHandle>.Success(new AsyncDisposableHandleAdapter(result.Value, resourceId));
    }
}
