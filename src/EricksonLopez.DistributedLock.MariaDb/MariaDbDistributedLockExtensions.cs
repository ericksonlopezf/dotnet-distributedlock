// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data.Common;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.MariaDb;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides extension methods for registering MariaDB distributed lock services in an <see cref="IServiceCollection"/>.
/// </summary>
public static class MariaDbDistributedLockExtensions
{
    /// <summary>
    /// Registers the MariaDB distributed lock provider with a connection factory delegate.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="connectionFactory">The delegate producing dedicated database connections.</param>
    /// <param name="configure">The optional configuration action for <see cref="MariaDbLockOptions"/>.</param>
    /// <returns>The configured service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="connectionFactory"/> is <see langword="null"/></exception>
    public static IServiceCollection AddMariaDbDistributedLock(
        this IServiceCollection services,
        Func<DbConnection> connectionFactory,
        Action<MariaDbLockOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionFactory);

        services.AddOptions();
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<IDistributedLockProvider>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<MariaDbDistributedLockProvider>>();
            var options = sp.GetRequiredService<IOptions<MariaDbLockOptions>>();
            return new MariaDbDistributedLockProvider(connectionFactory, logger, options);
        });

        return services;
    }

    /// <summary>
    /// Registers the MariaDB distributed lock provider using a connection string.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="connectionString">The connection string used to connect to MariaDB.</param>
    /// <param name="configure">The optional configuration action for <see cref="MariaDbLockOptions"/>.</param>
    /// <returns>The configured service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> is empty or contains only white space</exception>
    public static IServiceCollection AddMariaDbDistributedLock(
        this IServiceCollection services,
        string connectionString,
        Action<MariaDbLockOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return services.AddMariaDbDistributedLock(
            () => new MySqlConnection(connectionString),
            configure);
    }
}
