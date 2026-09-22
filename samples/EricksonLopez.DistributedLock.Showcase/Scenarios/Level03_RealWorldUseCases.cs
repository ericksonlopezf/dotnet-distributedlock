// Copyright © Erickson Lopez. MIT License.
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Showcase.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EricksonLopez.DistributedLock.Showcase.Scenarios;

/// <summary>
/// Level 3 — Real-World Use Cases: Banking double-spending prevention, webhook deduplication, and distributed jobs.
/// </summary>
public static class Level03_RealWorldUseCases
{
    public static async Task RunAsync()
    {
        ConsoleUi.PrintHeader("Level 3 — Real-World Use Cases",
            "Financial protection with ExecuteWithLockAsync<T> and strict event deduplication.");

        var dbPath = Path.Combine(Path.GetTempPath(), "showcase_usecases.db");
        var connectionString = $"Data Source={dbPath};";

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSqliteDistributedLock(connectionString);

        await using var sp = services.BuildServiceProvider();
        var lockProvider = sp.GetRequiredService<IDistributedLockProvider>();

        var storage = new SimulatedStorage();
        const string accountId = "ACC-98765";
        storage.SetBalance(accountId, 500.00m);

        ConsoleUi.PrintInfo($"Initial balance for account {accountId}: ${storage.GetBalance(accountId)}");

        // USE CASE 1: FINANCIAL DEBIT PROTECTED WITH ExecuteWithLockAsync<T>
        ConsoleUi.PrintInfo("Executing protected transfer with 'ExecuteWithLockAsync<decimal>'...");

        var debitResult = await lockProvider.ExecuteWithLockAsync(
            $"accounts:debit:{accountId}",
            async (ct) =>
            {
                var currentBalance = storage.GetBalance(accountId);
                const decimal debitAmount = 150.00m;

                if (currentBalance >= debitAmount)
                {
                    await Task.Delay(50, ct); // Simulate computation or I/O
                    storage.SetBalance(accountId, currentBalance - debitAmount);
                    return storage.GetBalance(accountId);
                }

                throw new InvalidOperationException("Insufficient funds.");
            });

        if (debitResult.IsSuccess)
        {
            ConsoleUi.PrintSuccess($"Debit processed atomically. New balance: ${debitResult.Value}");
        }
        else
        {
            ConsoleUi.PrintError($"Debit error: {debitResult.Error.Description}");
        }

        // USE CASE 2: WEBHOOK DEDUPLICATION
        const string webhookEventId = "evt_stripe_payment_intent_999";
        ConsoleUi.PrintInfo($"Simulating concurrent webhook reception '{webhookEventId}' across 2 workers...");

        var worker1Task = Task.Run(async () =>
        {
            return await lockProvider.ExecuteWithLockAsync(
                $"webhooks:{webhookEventId}",
                async (ct) =>
                {
                    ConsoleUi.PrintInfo("Worker 1: Processing webhook...");
                    await Task.Delay(100, ct);
                    return "Processed by Worker 1";
                });
        });

        var worker2Task = Task.Run(async () =>
        {
            await Task.Delay(15); // Arrives almost immediately after
            return await lockProvider.ExecuteWithLockAsync(
                $"webhooks:{webhookEventId}",
                async (ct) =>
                {
                    ConsoleUi.PrintInfo("Worker 2: Attempting to process webhook...");
                    return "Processed by Worker 2";
                });
        });

        var results = await Task.WhenAll(worker1Task, worker2Task);

        if (results[0].IsSuccess)
            ConsoleUi.PrintSuccess($"Worker 1 success: {results[0].Value}");
        else
            ConsoleUi.PrintWarning($"Worker 1 discarded: {results[0].Error.Code}");

        if (results[1].IsSuccess)
            ConsoleUi.PrintSuccess($"Worker 2 success: {results[1].Value}");
        else
            ConsoleUi.PrintWarning($"Worker 2 discarded as duplicate: {results[1].Error.Code}");

        try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { /* Ignore */ }
    }
}
