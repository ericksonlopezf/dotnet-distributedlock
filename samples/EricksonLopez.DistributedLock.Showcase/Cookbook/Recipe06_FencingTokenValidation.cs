// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Showcase.Infrastructure;

namespace EricksonLopez.DistributedLock.Showcase.Cookbook;

/// <summary>
/// Recipe 6: Downstream storage fencing token validation.
/// Problem: Protect databases or file storage against split-brain caused by slow clients or GC pauses.
/// </summary>
public static class Recipe06_FencingTokenValidation
{
    public static async Task RunAsync()
    {
        ConsoleUi.PrintHeader("Recipe 06: Monotonic Fencing Token Validation",
            "Usage of IDistributedLockHandle.FencingToken at target store.");

        var provider = new InMemoryDistributedLockProvider();
        var storage = new SimulatedStorage();
        const string resource = "documents:doc-101:version";

        var result = await provider.TryAcquireHandleAsync(resource);
        if (result.IsSuccess)
        {
            await using var handle = result.Value;
            var fencingToken = handle.FencingToken ?? 1L;

            ConsoleUi.PrintInfo($"Lock acquired. Issued FencingToken: {fencingToken}");

            var writeApplied = storage.TryWriteWithFencing(resource, fencingToken, () =>
            {
                ConsoleUi.PrintSuccess($"Write authorized by target store with FencingToken {fencingToken}.");
            });

            if (writeApplied)
            {
                ConsoleUi.PrintSuccess("Storage updated its monotonic upper bound.");
            }
        }
    }
}
