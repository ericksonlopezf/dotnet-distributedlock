// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Result;
using Result = EricksonLopez.Result.Result;

namespace EricksonLopez.DistributedLock.Abstractions;

/// <summary>
/// Provides extension methods for <see cref="IDistributedLockProvider"/> to execute operations within a distributed lock scope.
/// </summary>
public static class DistributedLockExtensions
{
    /// <summary>
    /// Executes an asynchronous action within the scope of an acquired distributed lock.
    /// </summary>
    /// <remarks>
    /// The lock handle is disposed upon completion. The delegate receives a token linked to <see cref="IDistributedLockHandle.HandleLostToken"/>.
    /// </remarks>
    /// <param name="provider">The distributed lock provider.</param>
    /// <param name="resourceId">The unique identifier of the resource to lock.</param>
    /// <param name="action">The asynchronous operation to execute within the lock scope.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a <see cref="Result{T}"/>
    /// with <see langword="true"/> if the lock was acquired and the operation executed;
    /// otherwise, the acquisition error.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> or <paramref name="action"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="resourceId"/> is empty or contains only white space</exception>
    public static async Task<Result<bool>> ExecuteWithLockAsync(
        this IDistributedLockProvider provider,
        string resourceId,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentNullException.ThrowIfNull(action);

        var acquireResult = await provider.TryAcquireHandleAsync(resourceId, cancellationToken).ConfigureAwait(false);
        if (acquireResult.IsFailure)
        {
            return Result<bool>.Failure(acquireResult.Error);
        }

        var handle = acquireResult.Value;
        await using (handle.ConfigureAwait(false))
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, handle.HandleLostToken);
            await action(linkedCts.Token).ConfigureAwait(false);
        }

        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Executes an asynchronous function returning a value within the scope of an acquired distributed lock.
    /// </summary>
    /// <remarks>
    /// The lock handle is disposed upon completion. The delegate receives a token linked to <see cref="IDistributedLockHandle.HandleLostToken"/>.
    /// </remarks>
    /// <typeparam name="T">The type of the result produced by the action.</typeparam>
    /// <param name="provider">The distributed lock provider.</param>
    /// <param name="resourceId">The unique identifier of the resource to lock.</param>
    /// <param name="action">The asynchronous function returning a value to execute within the lock scope.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a <see cref="Result{T}"/>
    /// with the value produced by the action if acquired; otherwise, the acquisition error.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> or <paramref name="action"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="resourceId"/> is empty or contains only white space</exception>
    public static async Task<Result<T>> ExecuteWithLockAsync<T>(
        this IDistributedLockProvider provider,
        string resourceId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentNullException.ThrowIfNull(action);

        var acquireResult = await provider.TryAcquireHandleAsync(resourceId, cancellationToken).ConfigureAwait(false);
        if (acquireResult.IsFailure)
        {
            return Result<T>.Failure(acquireResult.Error);
        }

        var handle = acquireResult.Value;
        await using (handle.ConfigureAwait(false))
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, handle.HandleLostToken);
            var value = await action(linkedCts.Token).ConfigureAwait(false);
            return Result<T>.Success(value);
        }
    }

    /// <summary>
    /// Executes an asynchronous action within the scope of a distributed lock, waiting up to the specified timeout.
    /// </summary>
    /// <remarks>
    /// The lock handle is disposed upon completion. The delegate receives a token linked to <see cref="IDistributedLockHandle.HandleLostToken"/>.
    /// </remarks>
    /// <param name="provider">The distributed lock provider.</param>
    /// <param name="resourceId">The unique identifier of the resource to lock.</param>
    /// <param name="timeout">The maximum duration to wait for lock acquisition.</param>
    /// <param name="action">The asynchronous operation to execute within the lock scope.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a <see cref="Result{T}"/>
    /// with <see langword="true"/> if the lock was acquired and the operation executed;
    /// otherwise, the acquisition error.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> or <paramref name="action"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="resourceId"/> is empty or contains only white space</exception>
    public static async Task<Result<bool>> ExecuteWithLockAsync(
        this IDistributedLockProvider provider,
        string resourceId,
        TimeSpan timeout,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentNullException.ThrowIfNull(action);

        var acquireResult = await provider.TryAcquireHandleAsync(resourceId, timeout, cancellationToken).ConfigureAwait(false);
        if (acquireResult.IsFailure)
        {
            return Result<bool>.Failure(acquireResult.Error);
        }

        var handle = acquireResult.Value;
        await using (handle.ConfigureAwait(false))
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, handle.HandleLostToken);
            await action(linkedCts.Token).ConfigureAwait(false);
        }

        return Result<bool>.Success(true);
    }

    /// <summary>
    /// Executes an asynchronous function returning a value within the scope of a distributed lock,
    /// waiting up to the specified timeout for acquisition.
    /// </summary>
    /// <remarks>
    /// The lock handle is disposed upon completion. The delegate receives a token linked to <see cref="IDistributedLockHandle.HandleLostToken"/>.
    /// </remarks>
    /// <typeparam name="T">The type of the result produced by the action.</typeparam>
    /// <param name="provider">The distributed lock provider.</param>
    /// <param name="resourceId">The unique identifier of the resource to lock.</param>
    /// <param name="timeout">The maximum duration to wait for lock acquisition.</param>
    /// <param name="action">The asynchronous function returning a value to execute within the lock scope.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>
    /// A task representing the asynchronous operation. The task result contains a <see cref="Result{T}"/>
    /// with the value produced by the action if the lock was acquired within the timeout;
    /// otherwise, the acquisition error.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> or <paramref name="action"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="resourceId"/> is empty or contains only white space</exception>
    public static async Task<Result<T>> ExecuteWithLockAsync<T>(
        this IDistributedLockProvider provider,
        string resourceId,
        TimeSpan timeout,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentNullException.ThrowIfNull(action);

        var acquireResult = await provider.TryAcquireHandleAsync(resourceId, timeout, cancellationToken).ConfigureAwait(false);
        if (acquireResult.IsFailure)
        {
            return Result<T>.Failure(acquireResult.Error);
        }

        var handle = acquireResult.Value;
        await using (handle.ConfigureAwait(false))
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, handle.HandleLostToken);
            var value = await action(linkedCts.Token).ConfigureAwait(false);
            return Result<T>.Success(value);
        }
    }
}
