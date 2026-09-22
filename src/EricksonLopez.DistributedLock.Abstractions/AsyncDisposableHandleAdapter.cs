// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.DistributedLock.Abstractions;

/// <summary>
/// Represents an internal fallback adapter for arbitrary <see cref="IAsyncDisposable"/> implementations.
/// </summary>
internal sealed class AsyncDisposableHandleAdapter(IAsyncDisposable inner, string resourceId) : IDistributedLockHandle
{
    /// <inheritdoc />
    public CancellationToken HandleLostToken => CancellationToken.None;

    /// <inheritdoc />
    public string ResourceId => resourceId;

    /// <inheritdoc />
    public long LockId => 0;

    /// <inheritdoc />
    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
