// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;

namespace EricksonLopez.DistributedLock.Abstractions;

/// <summary>
/// Defines a contract for a distributed lock handle whose disposal releases the acquired lock.
/// </summary>
public interface IDistributedLockHandle : IAsyncDisposable
{
    /// <summary>
    /// Gets a cancellation token that is canceled if lock ownership is lost unexpectedly.
    /// </summary>
    /// <remarks>
    /// Triggered by unexpected events such as connection drops, server failovers, or keepalive timeouts.
    /// </remarks>
    CancellationToken HandleLostToken { get; }

    /// <summary>
    /// Gets the unique identifier of the resource associated with this lock.
    /// </summary>
    string ResourceId { get; }

    /// <summary>
    /// Gets the 64-bit numerical identifier derived for this lock.
    /// </summary>
    /// <remarks>
    /// This value is an identifier derived deterministically from <see cref="ResourceId"/>, not a monotonic fencing token.
    /// For fencing verification against stale writes, see <see cref="FencingToken"/>.
    /// <para>
    /// <b>Sentinel value</b>: Adapter implementations (such as internal fallback handles) may return
    /// <c>0</c> as a sentinel value when no numeric lock identifier is applicable. Consumers that inspect
    /// <see cref="LockId"/> should treat <c>0</c> as an uninitialized or non-applicable identifier.
    /// </para>
    /// </remarks>
    long LockId { get; }

    /// <summary>
    /// Gets an optional monotonic fencing token associated with this lock acquisition.
    /// </summary>
    /// <remarks>
    /// Enables downstream resource validation against stale split-brain writes.
    /// Returns <see langword="null"/> if the underlying locking engine does not support monotonic fencing tokens.
    /// </remarks>
    long? FencingToken => null;
}
