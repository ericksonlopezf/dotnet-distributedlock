// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;

namespace EricksonLopez.DistributedLock.Showcase.Infrastructure;

/// <summary>
/// A simulated persistence store to demonstrate critical section state mutations and fencing token validation.
/// </summary>
public sealed class SimulatedStorage
{
    private readonly ConcurrentDictionary<string, decimal> _balances = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _highestFencingTokens = new(StringComparer.Ordinal);

    public void SetBalance(string accountId, decimal initialAmount)
    {
        _balances[accountId] = initialAmount;
    }

    public decimal GetBalance(string accountId)
    {
        return _balances.TryGetValue(accountId, out var balance) ? balance : 0m;
    }

    /// <summary>
    /// Attempts to apply a write protected by a fencing token.
    /// Rejects stale writes where the provided token is lower than or equal to the highest observed token.
    /// </summary>
    public bool TryWriteWithFencing(string resourceId, long fencingToken, Action writeAction)
    {
        while (true)
        {
            var currentHighest = _highestFencingTokens.GetOrAdd(resourceId, 0L);
            if (fencingToken <= currentHighest)
            {
                // Stale write detected: another client acquired a newer lock in the meantime
                return false;
            }

            if (_highestFencingTokens.TryUpdate(resourceId, fencingToken, currentHighest))
            {
                writeAction();
                return true;
            }
        }
    }
}
