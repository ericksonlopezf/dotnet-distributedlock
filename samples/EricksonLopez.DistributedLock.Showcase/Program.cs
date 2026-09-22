// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Showcase.Cookbook;
using EricksonLopez.DistributedLock.Showcase.Infrastructure;
using EricksonLopez.DistributedLock.Showcase.Scenarios;

namespace EricksonLopez.DistributedLock.Showcase;

/// <summary>
/// Main entry point for the EricksonLopez.DistributedLock Showcase application.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        ConsoleUi.PrintBanner();

        if (args.Length > 0)
        {
            var arg = args[0].ToLowerInvariant();

            if (arg is "--all" or "-a")
            {
                await RunAllAsync();
                return 0;
            }

            if (arg is "--level" or "-l" && args.Length > 1 && int.TryParse(args[1], out var level))
            {
                await RunLevelAsync(level);
                return 0;
            }

            if (arg is "--recipe" or "-r" && args.Length > 1 && int.TryParse(args[1], out var recipe))
            {
                await RunRecipeAsync(recipe);
                return 0;
            }

            ConsoleUi.PrintError($"Unknown argument: {arg}");
            PrintUsage();
            return 1;
        }

        // Interactive Mode
        return await RunInteractiveMenuAsync();
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
        CLI Usage:
          dotnet run --project samples/EricksonLopez.DistributedLock.Showcase -- [options]

        Options:
          --all, -a            Executes all progressive levels and recipes automatically in smoke-test mode.
          --level, -l <0-10>   Executes a specific progressive level.
          --recipe, -r <1-12>  Executes a specific practical recipe.
        """);
    }

    private static async Task<int> RunInteractiveMenuAsync()
    {
        while (true)
        {
            Console.WriteLine("""
            --------------------------------------------------------------------------------
            SHOWCASE MAIN MENU
            --------------------------------------------------------------------------------
            PROGRESSIVE LEVELS:
              0. Level 0 — Conceptual (Foundations & Architecture Comparison)
              1. Level 1 — Quick Start (DI Registration & First Acquisition)
              2. Level 2 — Full Configuration (Options, Jitter & OpenTelemetry)
              3. Level 3 — Real-World Use Cases (Banking & Webhook Deduplication)
              4. Level 4 — Transactional Integration (DbTransaction Scopes)
              5. Level 5 — Concurrent Processing & Contention
              6. Level 6 — ROP Error Handling (DistributedLockErrors)
              7. Level 7 — Lock Loss Detection (HandleLostToken)
              8. Level 8 — Fencing Tokens & Customization
              9. Level 9 — Multi-Engine Matrix (7 Official Dialects)
             10. Level 10 — Enterprise Architecture (Pipeline Behaviors)

            PRACTICAL RECIPES (COOKBOOK):
             11. Recipe 01 — Non-Blocking Immediate Acquisition
             12. Recipe 02 — Bounded Wait with Maximum Timeout
             13. Recipe 03 — Declarative Scope Guard (ExecuteWithLockAsync)
             14. Recipe 04 — Transaction-Bound Advisory Lock (DbTransaction)
             15. Recipe 05 — Proactive Lock Loss Detection in Background Workers
             16. Recipe 06 — Monotonic Fencing Token Validation
             17. Recipe 07 — Real-Time Observability with OpenTelemetry
             18. Recipe 08 — Dynamic Multi-Dialect Switching
             19. Recipe 09 — Declarative Processing with DistributedLockAttribute
             20. Recipe 10 — Graceful Cancellation & Shutdown Handling
             21. Recipe 11 — Blocking Overloads (AcquireAsync/AcquireHandleAsync)
             22. Recipe 12 — Blocking TransactionLockExtensions Overloads

             99. RUN ALL (Full Showcase Suite)
              Q. Quit
            --------------------------------------------------------------------------------
            """);
            Console.Write("Select an option: ");
            var input = Console.ReadLine()?.Trim().ToUpperInvariant();

            if (input is "Q" or "QUIT" or "EXIT")
            {
                ConsoleUi.PrintInfo("Exiting Showcase.");
                return 0;
            }

            if (input == "99")
            {
                await RunAllAsync();
                continue;
            }

            if (int.TryParse(input, out var choice))
            {
                if (choice is >= 0 and <= 10)
                {
                    await RunLevelAsync(choice);
                }
                else if (choice is >= 11 and <= 22)
                {
                    await RunRecipeAsync(choice - 10);
                }
                else
                {
                    ConsoleUi.PrintWarning("Invalid option.");
                }
            }
        }
    }

    private static async Task RunAllAsync()
    {
        ConsoleUi.PrintHeader("EXECUTING FULL SHOWCASE SUITE", "Automated validation and end-to-end demonstration mode.");

        for (var level = 0; level <= 10; level++)
        {
            await RunLevelAsync(level);
        }

        for (var recipe = 1; recipe <= 12; recipe++)
        {
            await RunRecipeAsync(recipe);
        }

        ConsoleUi.PrintHeader("FINAL SUMMARY", "All levels and recipes executed successfully with zero exceptions.");
        ConsoleUi.PrintSuccess("SHOWCASE AND EXECUTABLE REFERENCE ARCHITECTURE FULLY VALIDATED.");
    }

    private static async Task RunLevelAsync(int level)
    {
        switch (level)
        {
            case 0: await Level00_Conceptual.RunAsync(); break;
            case 1: await Level01_QuickStart.RunAsync(); break;
            case 2: await Level02_FullConfiguration.RunAsync(); break;
            case 3: await Level03_RealWorldUseCases.RunAsync(); break;
            case 4: await Level04_TransactionalIntegration.RunAsync(); break;
            case 5: await Level05_ConcurrentProcessing.RunAsync(); break;
            case 6: await Level06_ErrorHandling.RunAsync(); break;
            case 7: await Level07_HandleLostToken.RunAsync(); break;
            case 8: await Level08_FencingTokensAndCustomization.RunAsync(); break;
            case 9: await Level09_MultiEngineShowcase.RunAsync(); break;
            case 10: await Level10_EnterpriseArchitecture.RunAsync(); break;
            default: ConsoleUi.PrintError($"Level {level} not implemented."); break;
        }
    }

    private static async Task RunRecipeAsync(int recipe)
    {
        switch (recipe)
        {
            case 1: await Recipe01_NonBlockingAttempt.RunAsync(); break;
            case 2: await Recipe02_BoundedTimeoutRetry.RunAsync(); break;
            case 3: await Recipe03_SafeScopeGuard.RunAsync(); break;
            case 4: await Recipe04_TransactionalAdvisoryLock.RunAsync(); break;
            case 5: await Recipe05_WorkerLossDetection.RunAsync(); break;
            case 6: await Recipe06_FencingTokenValidation.RunAsync(); break;
            case 7: await Recipe07_OpenTelemetryIntegration.RunAsync(); break;
            case 8: await Recipe08_MultiDbDialectSwitching.RunAsync(); break;
            case 9: await Recipe09_DeclarativeAttributeProcessor.RunAsync(); break;
            case 10: await Recipe10_HandlingCancellationAndShutdown.RunAsync(); break;
            case 11: await Recipe11_BlockingAcquireOverloads.RunAsync(); break;
            case 12: await Recipe12_TransactionLockBlockingOverloads.RunAsync(); break;
            default: ConsoleUi.PrintError($"Recipe {recipe} not implemented."); break;
        }
    }
}
