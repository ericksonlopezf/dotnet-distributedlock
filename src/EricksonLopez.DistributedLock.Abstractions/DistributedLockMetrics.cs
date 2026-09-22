// Copyright © Erickson Lopez. MIT License.
using System.Collections.Generic;
using System.Diagnostics.Metrics;

namespace EricksonLopez.DistributedLock.Abstractions;

/// <summary>
/// Provides OpenTelemetry instrumentation and metrics for distributed lock operations.
/// </summary>
public static class DistributedLockMetrics
{
    /// <summary>
    /// Gets the name of the OpenTelemetry meter used for distributed lock telemetry.
    /// </summary>
    public const string MeterName = "EricksonLopez.DistributedLock";

    /// <summary>
    /// Gets the version of the OpenTelemetry meter used for distributed lock telemetry.
    /// </summary>
    public const string MeterVersion = "1.0.0";

    private static readonly Meter Meter = new(MeterName, MeterVersion);

    private static readonly Counter<long> AcquisitionsCounter = Meter.CreateCounter<long>(
        name: "distributed_lock.acquisitions",
        unit: "{acquisition}",
        description: "Number of distributed lock acquisition attempts categorized by outcome.");

    private static readonly Histogram<double> HoldDurationHistogram = Meter.CreateHistogram<double>(
        name: "distributed_lock.hold_duration",
        unit: "ms",
        description: "Duration in milliseconds for which an acquired distributed lock was held.");

    private static readonly Histogram<double> WaitDurationHistogram = Meter.CreateHistogram<double>(
        name: "distributed_lock.wait_duration",
        unit: "ms",
        description: "Duration in milliseconds spent attempting to acquire the distributed lock.");

    private static readonly Counter<long> LockLostCounter = Meter.CreateCounter<long>(
        name: "distributed_lock.lost",
        unit: "{lock}",
        description: "Number of held distributed locks lost unexpectedly due to socket or session severance.");

    /// <summary>
    /// Records a distributed lock acquisition attempt and its outcome.
    /// </summary>
    /// <param name="resourceId">The unique identifier of the resource for which the lock was attempted.</param>
    /// <param name="lockType">The type of lock attempted. Canonical values include "session" or "transaction".</param>
    /// <param name="status">The outcome status of the acquisition attempt.</param>
    public static void RecordAcquisition(string resourceId, string lockType, string status)
    {
        AcquisitionsCounter.Add(1,
            new KeyValuePair<string, object?>("resource_id", resourceId),
            new KeyValuePair<string, object?>("lock_type", lockType),
            new KeyValuePair<string, object?>("status", status));
    }

    /// <summary>
    /// Records the duration spent waiting to acquire a distributed lock.
    /// </summary>
    /// <param name="resourceId">The unique identifier of the resource for which the lock was attempted.</param>
    /// <param name="lockType">The type of lock attempted. Canonical values include "session" or "transaction".</param>
    /// <param name="status">The outcome status of the acquisition attempt.</param>
    /// <param name="durationMs">The elapsed duration in milliseconds spent waiting.</param>
    public static void RecordWaitDuration(string resourceId, string lockType, string status, double durationMs)
    {
        WaitDurationHistogram.Record(durationMs,
            new KeyValuePair<string, object?>("resource_id", resourceId),
            new KeyValuePair<string, object?>("lock_type", lockType),
            new KeyValuePair<string, object?>("status", status));
    }

    /// <summary>
    /// Records the duration for which an acquired distributed lock was held prior to disposal.
    /// </summary>
    /// <param name="resourceId">The unique identifier of the resource whose lock was held.</param>
    /// <param name="lockType">The type of lock held. Canonical values include "session" or "transaction".</param>
    /// <param name="durationMs">The duration in milliseconds the lock was held.</param>
    public static void RecordHoldDuration(string resourceId, string lockType, double durationMs)
    {
        HoldDurationHistogram.Record(durationMs,
            new KeyValuePair<string, object?>("resource_id", resourceId),
            new KeyValuePair<string, object?>("lock_type", lockType));
    }

    /// <summary>
    /// Records an unexpected loss of distributed lock ownership.
    /// </summary>
    /// <param name="resourceId">The unique identifier of the resource whose lock was lost.</param>
    /// <param name="lockId">The 64-bit numerical identifier of the lost lock.</param>
    public static void RecordLockLost(string resourceId, long lockId)
    {
        LockLostCounter.Add(1,
            new KeyValuePair<string, object?>("resource_id", resourceId),
            new KeyValuePair<string, object?>("lock_id", lockId));
    }
}
