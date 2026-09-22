// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.DistributedLock.Sqlite;

/// <summary>
/// Represents configuration options for <see cref="SqliteDistributedLockProvider"/>.
/// </summary>
public sealed class SqliteLockOptions
{
    /// <summary>
    /// Gets or sets the command timeout in seconds for lock queries.
    /// The default value is 30 seconds.
    /// </summary>
    public int CommandTimeoutSeconds { get; set; } = 30;

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

    /// <summary>
    /// Gets or sets the lock lease time-to-live duration.
    /// Locks exceeding this duration without renewal or release are considered orphaned and eligible for reclamation.
    /// The default value is 60 seconds.
    /// </summary>
    public TimeSpan LockTtl { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets the cadence for background renewal of the lock lease.
    /// When <see langword="null"/>, defaults to one-third of <see cref="LockTtl"/>.
    /// </summary>
    public TimeSpan? KeepaliveCadence { get; set; }
}
