// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.DistributedLock.Abstractions;

/// <summary>
/// Specifies declarative distributed mutual exclusion for target classes or methods.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class DistributedLockAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DistributedLockAttribute"/> class with the specified resource key pattern.
    /// </summary>
    /// <param name="resourceKeyPattern">The template or literal key identifying the resource to lock.</param>
    /// <exception cref="ArgumentException"><paramref name="resourceKeyPattern"/> is empty or contains only white space</exception>
    public DistributedLockAttribute(string resourceKeyPattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceKeyPattern);
        ResourceKeyPattern = resourceKeyPattern;
    }

    /// <summary>
    /// Gets the template or literal key pattern for the resource.
    /// </summary>
    public string ResourceKeyPattern { get; }

    /// <summary>
    /// Gets or sets the maximum duration in seconds to wait for lock acquisition.
    /// </summary>
    /// <remarks>
    /// A value of zero specifies an immediate, non-blocking acquisition attempt. Defaults to zero.
    /// </remarks>
    public int TimeoutSeconds { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether lock acquisition should block until granted.
    /// </summary>
    /// <remarks>
    /// When set to <see langword="true"/>, the operation blocks until granted or until bounded by <see cref="TimeoutSeconds"/>.
    /// </remarks>
    public bool Blocking { get; set; }
}
