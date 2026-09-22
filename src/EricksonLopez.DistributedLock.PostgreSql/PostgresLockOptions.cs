// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.DistributedLock.PostgreSql;

/// <summary>
/// Specifies configuration options for PostgreSQL distributed locks.
/// </summary>
public sealed class PostgresLockOptions
{
    /// <summary>
    /// Gets or sets the cadence for background keepalive ping operations on session-level locks.
    /// </summary>
    /// <remarks>
    /// Set to <see cref="TimeSpan.Zero"/> or negative to disable keepalive heartbeats. Defaults to 30 seconds.
    /// </remarks>
    public TimeSpan KeepaliveCadence { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the initial interval to wait between polling attempts when acquiring locks under contention.
    /// </summary>
    /// <remarks>
    /// Defaults to 25 milliseconds.
    /// </remarks>
    public TimeSpan InitialPollingInterval { get; set; } = TimeSpan.FromMilliseconds(25);

    /// <summary>
    /// Gets or sets the maximum interval ceiling for polling attempts during exponential backoff.
    /// </summary>
    /// <remarks>
    /// Defaults to 250 milliseconds.
    /// </remarks>
    public TimeSpan MaxPollingInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Gets or sets the jitter ratio applied to polling delays to prevent thundering herd contention.
    /// </summary>
    /// <remarks>
    /// Must be between 0.0 (no jitter) and 1.0 (full jitter). Defaults to 0.25 (25% jitter).
    /// </remarks>
    public double JitterRatio { get; set; } = 0.25;

    /// <summary>
    /// Gets or sets the command timeout in seconds for database advisory lock queries.
    /// </summary>
    /// <remarks>
    /// A value of <see langword="null"/> relies on the default connection command timeout.
    /// </remarks>
    public int? CommandTimeoutSeconds { get; set; }
}
