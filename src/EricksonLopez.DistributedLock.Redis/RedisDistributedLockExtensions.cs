// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Redis;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides extension methods for registering Redis distributed lock services with an <see cref="IServiceCollection"/>.
/// </summary>
public static class RedisDistributedLockExtensions
{
    /// <summary>
    /// Registers Redis distributed lock services using the specified connection multiplexer.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="multiplexer">The connection multiplexer used to communicate with Redis.</param>
    /// <param name="configure">An optional delegate to configure <see cref="RedisLockOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance so that multiple calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="multiplexer"/> is <see langword="null"/></exception>
    public static IServiceCollection AddRedisDistributedLock(
        this IServiceCollection services,
        IConnectionMultiplexer multiplexer,
        Action<RedisLockOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(multiplexer);

        services.AddOptions();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IDistributedLockProvider>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<RedisDistributedLockProvider>>();
            var options = sp.GetRequiredService<IOptions<RedisLockOptions>>();
            return new RedisDistributedLockProvider(multiplexer, logger, options);
        });

        return services;
    }

    /// <summary>
    /// Registers Redis distributed lock services resolving the required <see cref="IConnectionMultiplexer"/> from the service provider.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="configure">An optional delegate to configure <see cref="RedisLockOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance so that multiple calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    public static IServiceCollection AddRedisDistributedLock(
        this IServiceCollection services,
        Action<RedisLockOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IDistributedLockProvider>(sp =>
        {
            var multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();
            var logger = sp.GetRequiredService<ILogger<RedisDistributedLockProvider>>();
            var options = sp.GetRequiredService<IOptions<RedisLockOptions>>();
            return new RedisDistributedLockProvider(multiplexer, logger, options);
        });

        return services;
    }
}
