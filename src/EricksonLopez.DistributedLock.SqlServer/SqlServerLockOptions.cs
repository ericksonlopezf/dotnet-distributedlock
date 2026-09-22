// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.DistributedLock.SqlServer;

/// <summary>
/// Represents configuration options for <see cref="SqlServerDistributedLockProvider"/>.
/// </summary>
public sealed class SqlServerLockOptions
{
    /// <summary>
    /// Gets or sets the command timeout in seconds for advisory lock queries.
    /// The default value is 30 seconds.
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the cadence for sending keepalive heartbeat queries over held session connections.
    /// When <see langword="null"/>, keepalives are disabled. The default value is 10 seconds.
    /// </summary>
    public TimeSpan? KeepaliveCadence { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the base polling interval when waiting for a lock with timeout.
    /// The default value is 50 milliseconds.
    /// </summary>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Gets or sets a value indicating whether random jitter is added to retry intervals.
    /// The default value is <see langword="true"/>.
    /// </summary>
    public bool BackoffJitter { get; set; } = true;
}
