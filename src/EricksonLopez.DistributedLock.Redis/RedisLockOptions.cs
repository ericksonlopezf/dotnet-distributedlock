// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.DistributedLock.Redis;

/// <summary>
/// Represents configuration options for <see cref="RedisDistributedLockProvider"/>.
/// </summary>
public sealed class RedisLockOptions
{
    /// <summary>
    /// Gets or sets the default lease duration for acquired locks.
    /// The default value is 30 seconds.
    /// </summary>
    public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the key prefix prepended to resource identifiers in Redis.
    /// The default value is <c>"lock:"</c>.
    /// </summary>
    public string KeyPrefix { get; set; } = "lock:";

    /// <summary>
    /// Gets or sets the cadence for renewing lock leases before expiration.
    /// The default value is 10 seconds.
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
