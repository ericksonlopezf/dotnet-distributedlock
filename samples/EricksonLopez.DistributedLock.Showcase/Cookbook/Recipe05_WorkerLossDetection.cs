// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Showcase.Infrastructure;

namespace EricksonLopez.DistributedLock.Showcase.Cookbook;

/// <summary>
/// Recipe 5: Proactive lock loss detection in Background Workers.
/// Problem: If your worker is running a heavy task and the database server restarts or network is lost, the worker must abort immediately.
/// </summary>
public static class Recipe05_WorkerLossDetection
{
    public static async Task RunAsync()
    {
        ConsoleUi.PrintHeader("Recipe 05: Proactive Loss Detection in Background Workers",
            "Linking CancellationTokenSource with IDistributedLockHandle.HandleLostToken.");

        var provider = new InMemoryDistributedLockProvider();
        var handleResult = await provider.TryAcquireHandleAsync("workers:heavy-data-sync");

        if (handleResult.IsSuccess)
        {
            var handle = handleResult.Value;
            await using (handle)
            {
                using var hostShutdownCts = new CancellationTokenSource();
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                    hostShutdownCts.Token,
                    handle.HandleLostToken);

                ConsoleUi.PrintSuccess("Worker started with 'HandleLostToken' linked.");

                // Periodic check in work loop
                if (!linkedCts.Token.IsCancellationRequested)
                {
                    ConsoleUi.PrintInfo("Worker executing safe processing loop...");
                    await Task.Delay(25, linkedCts.Token);
                    ConsoleUi.PrintSuccess("Loop completed with verified lock ownership.");
                }
            }
        }
    }
}
