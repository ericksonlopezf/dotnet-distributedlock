// Copyright © Erickson Lopez. MIT License.
using System;
using System.Reflection;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Showcase.Infrastructure;

namespace EricksonLopez.DistributedLock.Showcase.Cookbook;

/// <summary>
/// Recipe 9: Declarative processing via DistributedLockAttribute.
/// Problem: Decorate services or methods with attributes to decouple locking from business logic.
/// </summary>
public static class Recipe09_DeclarativeAttributeProcessor
{
    public static async Task RunAsync()
    {
        ConsoleUi.PrintHeader("Recipe 09: Declarative Processing with DistributedLockAttribute",
            "Dynamic interpretation of attributes in application services.");

        var provider = new InMemoryDistributedLockProvider();
        var service = new OrderProcessingService();

        var method = typeof(OrderProcessingService).GetMethod(nameof(OrderProcessingService.ProcessOrderAsync));
        var lockAttr = method?.GetCustomAttribute<DistributedLockAttribute>();

        if (lockAttr is not null)
        {
            ConsoleUi.PrintInfo($"Attribute detected on {method!.Name}: Pattern='{lockAttr.ResourceKeyPattern}', Timeout={lockAttr.TimeoutSeconds}s");
            const string orderId = "ORD-777";
            var key = lockAttr.ResourceKeyPattern.Replace("{OrderId}", orderId, StringComparison.Ordinal);

            var result = await provider.ExecuteWithLockAsync(key, async (ct) =>
            {
                await service.ProcessOrderAsync(orderId);
            });

            if (result.IsSuccess)
            {
                ConsoleUi.PrintSuccess("Decorated method executed inside lock.");
            }
        }
    }

    private sealed class OrderProcessingService
    {
        [DistributedLock("orders:{OrderId}:process", TimeoutSeconds = 5)]
        public Task ProcessOrderAsync(string orderId)
        {
            ConsoleUi.PrintInfo($"[OrderProcessingService] Processing order {orderId}...");
            return Task.CompletedTask;
        }
    }
}
