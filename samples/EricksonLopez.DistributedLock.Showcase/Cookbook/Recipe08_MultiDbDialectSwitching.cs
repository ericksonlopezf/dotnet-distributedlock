// Copyright © Erickson Lopez. MIT License.
using System;
using System.IO;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Showcase.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EricksonLopez.DistributedLock.Showcase.Cookbook;

/// <summary>
/// Recipe 8: Dynamic multi-dialect database switching based on environment.
/// Problem: You want to use SQLite for local tests and development, but connect to PostgreSQL or SQL Server in production.
/// </summary>
public static class Recipe08_MultiDbDialectSwitching
{
    public static async Task RunAsync()
    {
        ConsoleUi.PrintHeader("Recipe 08: Dynamic Provider Switching",
            "Conditional DI configuration based on runtime environment (Local vs Production).");

        var dbPath = Path.Combine(Path.GetTempPath(), "recipe08.db");

        // Read environment variable: "Development" or "Production"
        var currentEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var services = new ServiceCollection();
        services.AddLogging();

        if (currentEnvironment == "Development")
        {
            ConsoleUi.PrintInfo("Local environment detected: Registering SQLite...");
            services.AddSqliteDistributedLock($"Data Source={dbPath};");
        }
        else
        {
            ConsoleUi.PrintInfo("Production environment detected: Registering PostgreSQL...");
            // services.AddPostgresDistributedLock(sp => new NpgsqlConnection(prodConnStr));
        }

        await using var sp = services.BuildServiceProvider();
        var provider = sp.GetRequiredService<IDistributedLockProvider>();

        ConsoleUi.PrintSuccess($"Provider resolved for '{currentEnvironment}': {provider.GetType().Name}");

        var result = await provider.TryAcquireAsync("env:test:resource");
        if (result.IsSuccess)
        {
            await using (result.Value)
            {
                ConsoleUi.PrintSuccess("Lock executed identically regardless of active engine.");
            }
        }

        try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { /* Ignore */ }
    }
}
