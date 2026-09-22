// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Showcase.Infrastructure;

namespace EricksonLopez.DistributedLock.Showcase.Scenarios;

/// <summary>
/// Level 8 — Fencing Tokens & Customization: Protection against stale writes and custom provider implementations.
/// </summary>
public static class Level08_FencingTokensAndCustomization
{
    public static async Task RunAsync()
    {
        ConsoleUi.PrintHeader("Level 8 — Fencing Tokens and Customization",
            "Monotonic token validation against GC pauses (Martin Kleppmann) and custom provider implementation.");

        // 1. Instantiate custom in-memory provider
        var provider = new InMemoryDistributedLockProvider();
        var storage = new SimulatedStorage();
        const string resourceId = "orders:order-456:state";

        ConsoleUi.PrintInfo("Simulating 'Stale Write' scenario (Zombie write after GC pause):");

        // Client 1 acquires lock with Fencing Token 1
        var client1Acquire = await provider.TryAcquireHandleAsync(resourceId);
        if (client1Acquire.IsSuccess)
        {
            var handle1 = client1Acquire.Value;
            var token1 = handle1.FencingToken ?? 1L;
            ConsoleUi.PrintSuccess($"Client 1 acquired lock with Fencing Token: {token1}");

            // Simulate Client 1 entering a long pause (e.g. Full GC STW or VM freeze)
            ConsoleUi.PrintInfo("Client 1 enters long pause (STW GC)...");

            // Meanwhile, the lock expires or is released, and Client 2 acquires lock with Fencing Token 2
            await handle1.DisposeAsync();

            var client2Acquire = await provider.TryAcquireHandleAsync(resourceId);
            if (client2Acquire.IsSuccess)
            {
                var handle2 = client2Acquire.Value;
                var token2 = handle2.FencingToken ?? 2L;
                ConsoleUi.PrintSuccess($"Client 2 acquired lock with new Fencing Token: {token2}");

                // Client 2 writes to storage with valid token
                var write2Success = storage.TryWriteWithFencing(resourceId, token2, () =>
                {
                    ConsoleUi.PrintSuccess("Client 2: Write persisted successfully.");
                });

                if (write2Success)
                {
                    ConsoleUi.PrintSuccess("Storage updated highest token to 2.");
                }

                await handle2.DisposeAsync();
            }

            // Now Client 1 'wakes up' and attempts to write to storage with its stale token 1
            ConsoleUi.PrintInfo("Client 1 wakes up from pause and attempts write with stale token (1)...");
            var write1Success = storage.TryWriteWithFencing(resourceId, token1, () =>
            {
                ConsoleUi.PrintError("ERROR: Client 1 illegitimately overwrote data (Split-Brain write)!");
            });

            if (!write1Success)
            {
                ConsoleUi.PrintSuccess("PROTECTION CONFIRMED: Storage REJECTED Client 1 write because Fencing Token (1) was lower than registered token (2).");
            }
        }
    }
}
