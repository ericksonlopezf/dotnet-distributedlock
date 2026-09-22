// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Showcase.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EricksonLopez.DistributedLock.Showcase.Scenarios;

/// <summary>
/// Level 5 — Concurrent Processing & Contention: High concurrency, retries with jitter, and cancellation.
/// </summary>
public static class Level05_ConcurrentProcessing
{
    public static async Task RunAsync()
    {
        ConsoleUi.PrintHeader("Level 5 — Concurrent Processing and Contention",
            "10 concurrent workers competing for a single resource with bounded timeouts and jittered backoff.");

        var dbPath = Path.Combine(Path.GetTempPath(), "showcase_concurrency.db");
        var connectionString = $"Data Source={dbPath};";

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSqliteDistributedLock(connectionString, o =>
        {
            o.RetryInterval = TimeSpan.FromMilliseconds(20);
            o.BackoffJitter = true;
        });

        await using var sp = services.BuildServiceProvider();
        var lockProvider = sp.GetRequiredService<IDistributedLockProvider>();

        const string hotResource = "inventory:reserve:SKU-4040";
        const int totalWorkers = 10;
        var activeHoldersCount = 0;
        var maxObservedConcurrency = 0;
        var successfulAcquisitions = 0;
        var timedOutAcquisitions = 0;

        ConsoleUi.PrintInfo($"Launching {totalWorkers} parallel workers on '{hotResource}'...");
        var sw = Stopwatch.StartNew();

        var tasks = new List<Task>();
        for (var i = 1; i <= totalWorkers; i++)
        {
            var workerId = i;
            tasks.Add(Task.Run(async () =>
            {
                // Each worker attempts to acquire with a 400 ms timeout
                var result = await lockProvider.TryAcquireAsync(hotResource, TimeSpan.FromMilliseconds(400));
                if (result.IsSuccess)
                {
                    await using (result.Value)
                    {
                        var current = Interlocked.Increment(ref activeHoldersCount);
                        lock (tasks)
                        {
                            if (current > maxObservedConcurrency)
                            {
                                maxObservedConcurrency = current;
                            }
                        }

                        // Simulate protected processing
                        await Task.Delay(50);

                        Interlocked.Decrement(ref activeHoldersCount);
                        Interlocked.Increment(ref successfulAcquisitions);
                        ConsoleUi.PrintInfo($"Worker #{workerId} completed work successfully.");
                    }
                }
                else
                {
                    Interlocked.Increment(ref timedOutAcquisitions);
                    ConsoleUi.PrintWarning($"Worker #{workerId} timeout/contention: {result.Error.Code}");
                }
            }));
        }

        await Task.WhenAll(tasks);
        sw.Stop();

        ConsoleUi.PrintMetric("Total execution time", sw.ElapsedMilliseconds, "ms");
        ConsoleUi.PrintMetric("Successful workers", successfulAcquisitions);
        ConsoleUi.PrintMetric("Timed out workers", timedOutAcquisitions);
        ConsoleUi.PrintMetric("Max observed concurrency in critical section", maxObservedConcurrency);

        if (maxObservedConcurrency == 1)
        {
            ConsoleUi.PrintSuccess("PERFECT MUTUAL EXCLUSION: At no point was there more than 1 worker in critical section.");
        }
        else
        {
            ConsoleUi.PrintError($"MUTEX VIOLATION: Detected {maxObservedConcurrency} concurrent workers.");
        }

        try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { /* Ignore */ }
    }
}
