// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.DistributedLock.Oracle;

/// <summary>
/// Specifies configuration options for the Oracle Database distributed lock provider using DBMS_LOCK.
/// </summary>
public sealed class OracleLockOptions
{
    /// <summary>
    /// Gets or sets the command timeout in seconds for lock maintenance commands.
    /// </summary>
    /// <remarks>
    /// The default value is 30 seconds.
    /// </remarks>
    public int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets or sets the cadence for sending keepalive heartbeat queries over held session connections.
    /// </summary>
    /// <remarks>
    /// When set to <see langword="null"/>, keepalive heartbeats are disabled. Defaults to 10 seconds.
    /// </remarks>
    public TimeSpan? KeepaliveCadence { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets the base polling interval when waiting for a lock with timeout.
    /// </summary>
    /// <remarks>
    /// The default value is 50 milliseconds.
    /// </remarks>
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Gets or sets a value indicating whether random jitter is added to retry intervals.
    /// </summary>
    /// <remarks>
    /// The default value is <see langword="true"/>.
    /// </remarks>
    public bool BackoffJitter { get; set; } = true;
}
