// Copyright © Erickson Lopez. MIT License.
using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Showcase.Infrastructure;

namespace EricksonLopez.DistributedLock.Showcase.Scenarios;

/// <summary>
/// Level 10 — Enterprise Architecture: Declarative coordination via DistributedLockAttribute and Pipeline Behaviors.
/// </summary>
public static class Level10_EnterpriseArchitecture
{
    public static async Task RunAsync()
    {
        ConsoleUi.PrintHeader("Level 10 — Enterprise Architecture",
            "Decorators, Pipeline Behaviors, and declarative coordination without invasive coupling.");

        var lockProvider = new InMemoryDistributedLockProvider();
        var dispatcher = new EnterpriseCommandDispatcher(lockProvider);

        var command = new ProcessPayoutCommand("payout-9871", 2500.00m);

        ConsoleUi.PrintInfo($"Dispatching business command: {command.GetType().Name} for {command.PayoutId}...");
        var result = await dispatcher.DispatchAsync(command, async (cmd, ct) =>
        {
            ConsoleUi.PrintInfo($"Executing protected business handler for Payout {cmd.PayoutId}...");
            await Task.Delay(50, ct);
            return $"Payout of ${cmd.Amount} executed successfully.";
        });

        if (result.IsSuccess)
        {
            ConsoleUi.PrintSuccess($"Pipeline result: {result.Value}");
        }
        else
        {
            ConsoleUi.PrintError($"Pipeline failure: {result.Error.Description}");
        }
    }

    /// <summary>
    /// Command declaratively annotated with the official library attribute.
    /// </summary>
    [DistributedLock("payouts:{PayoutId}", TimeoutSeconds = 3, Blocking = true)]
    public sealed record ProcessPayoutCommand(string PayoutId, decimal Amount);

    /// <summary>
    /// Enterprise Pipeline Dispatcher that automatically interprets DistributedLockAttribute.
    /// </summary>
    public sealed class EnterpriseCommandDispatcher
    {
        private readonly IDistributedLockProvider _provider;

        public EnterpriseCommandDispatcher(IDistributedLockProvider provider)
        {
            _provider = provider;
        }

        public async Task<EricksonLopez.Result.Result<TResponse>> DispatchAsync<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] TCommand, TResponse>(
            TCommand command,
            Func<TCommand, CancellationToken, Task<TResponse>> handler,
            CancellationToken cancellationToken = default)
            where TCommand : notnull
        {
            var lockAttr = typeof(TCommand).GetCustomAttribute<DistributedLockAttribute>();
            if (lockAttr is null)
            {
                // Without lock, execute directly
                return await handler(command, cancellationToken);
            }

            // Resolve key from template
            var resourceKey = ResolveTemplate(lockAttr.ResourceKeyPattern, command);
            ConsoleUi.PrintInfo($"[Pipeline] Attribute detected. Resolved key: '{resourceKey}'. Timeout: {lockAttr.TimeoutSeconds}s, Blocking: {lockAttr.Blocking}");

            var timeout = lockAttr.TimeoutSeconds > 0
                ? TimeSpan.FromSeconds(lockAttr.TimeoutSeconds)
                : TimeSpan.Zero;

            var acquireResult = await _provider.TryAcquireHandleAsync(resourceKey, timeout, cancellationToken).ConfigureAwait(false);
            if (acquireResult.IsFailure)
            {
                return acquireResult.Error;
            }

            var handle = acquireResult.Value;
            await using (handle.ConfigureAwait(false))
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, handle.HandleLostToken);
                var value = await handler(command, linkedCts.Token).ConfigureAwait(false);
                return EricksonLopez.Result.Result<TResponse>.Success(value);
            }
        }

        private static string ResolveTemplate<[System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)] TCommand>(string template, TCommand command)
        {
            var resolved = template;
            foreach (var prop in typeof(TCommand).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var placeholder = $"{{{prop.Name}}}";
                if (resolved.Contains(placeholder, StringComparison.OrdinalIgnoreCase))
                {
                    var val = prop.GetValue(command)?.ToString() ?? string.Empty;
                    resolved = resolved.Replace(placeholder, val, StringComparison.OrdinalIgnoreCase);
                }
            }
            return resolved;
        }
    }
}
