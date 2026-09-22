// Copyright © Erickson Lopez. MIT License.
using EricksonLopez.Result;

namespace EricksonLopez.DistributedLock.Abstractions;

/// <summary>
/// Provides predefined standard domain errors for distributed locking failures.
/// </summary>
public static class DistributedLockErrors
{
    /// <summary>
    /// Represents a domain error indicating that the requested lock is already held by another owner.
    /// </summary>
    public static readonly Error LockAlreadyHeld = Error.Conflict(
        "DistributedLock.AlreadyHeld",
        "The distributed lock is currently held by another process.");

    /// <summary>
    /// Represents a domain error indicating that the lock could not be acquired within the allocated duration.
    /// </summary>
    public static readonly Error Timeout = Error.Failure(
        "DistributedLock.Timeout",
        "The distributed lock could not be acquired within the allocated timeout.");

    /// <summary>
    /// Represents a domain error indicating that lock ownership was lost unexpectedly due to session or network termination.
    /// </summary>
    public static readonly Error LockLost = Error.Failure(
        "DistributedLock.Lost",
        "The distributed lock was lost unexpectedly due to session or network termination.");

    /// <summary>
    /// Represents a domain error indicating that the lock acquisition operation was canceled.
    /// </summary>
    public static readonly Error Canceled = Error.Failure(
        "DistributedLock.Canceled",
        "The distributed lock acquisition was canceled.");
}
