// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;

namespace EricksonLopez.DistributedLock.PostgreSql;

/// <summary>
/// Represents a no-operation disposable handle returned for transaction-bound locks.
/// Transaction-bound locks are released automatically when the transaction commits or rolls back.
/// </summary>
/// <remarks>
/// Because transaction-bound locks do not hold a dedicated connection, there is no keepalive heartbeat
/// and no mechanism to signal premature lock loss. Consequently, <see cref="HandleLostToken"/> returns
/// <see cref="CancellationToken.None"/>. Consumers should link cancellation to the transaction lifetime.
/// <para>
/// <b>Implementation note</b>: Although this class is <see langword="public"/>, it is an implementation
/// detail of <see cref="PostgresDistributedLockProvider"/>. Consumers should interact with this handle
/// through the <see cref="IDistributedLockHandle"/> interface, not by constructing it directly.
/// </para>
/// </remarks>
/// <param name="resourceId">The resource identifier string for the lock, used for identification purposes.</param>
/// <param name="lockId">The 64-bit numeric lock identifier associated with this handle; typically derived from <paramref name="resourceId"/> by the caller using <see cref="PostgresDistributedLockProvider.GenerateLockId"/>.</param>
public sealed class NoOpAsyncDisposable(string resourceId = "", long lockId = 0) : IDistributedLockHandle
{
    /// <summary>
    /// Gets a cancellation token that is never canceled for transaction-scoped locks.
    /// </summary>
    /// <remarks>
    /// Transaction-bound locks do not use a keepalive heartbeat; their lifecycle is governed
    /// entirely by the enclosing database transaction. This token is never signaled.
    /// </remarks>
    public CancellationToken HandleLostToken => CancellationToken.None;

    /// <inheritdoc />
    public string ResourceId => resourceId;

    /// <inheritdoc />
    public long LockId => lockId;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
