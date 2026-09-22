// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data.Common;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.MySql;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides extension methods for registering MySQL distributed lock services in an <see cref="IServiceCollection"/>.
/// </summary>
public static class MySqlDistributedLockExtensions
{
    /// <summary>
    /// Registers the MySQL distributed lock provider with a connection factory delegate.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="connectionFactory">The delegate producing dedicated database connections.</param>
    /// <param name="configure">The optional configuration action for <see cref="MySqlLockOptions"/>.</param>
    /// <returns>The configured service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="connectionFactory"/> is <see langword="null"/></exception>
    public static IServiceCollection AddMySqlDistributedLock(
        this IServiceCollection services,
        Func<DbConnection> connectionFactory,
        Action<MySqlLockOptions>? configure = null)
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
            var logger = sp.GetRequiredService<ILogger<MySqlDistributedLockProvider>>();
            var options = sp.GetRequiredService<IOptions<MySqlLockOptions>>();
            return new MySqlDistributedLockProvider(connectionFactory, logger, options);
        });

        return services;
    }

    /// <summary>
    /// Registers the MySQL distributed lock provider using a connection string.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="connectionString">The connection string used to connect to MySQL.</param>
    /// <param name="configure">The optional configuration action for <see cref="MySqlLockOptions"/>.</param>
    /// <returns>The configured service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> is empty or contains only white space</exception>
    public static IServiceCollection AddMySqlDistributedLock(
        this IServiceCollection services,
        string connectionString,
        Action<MySqlLockOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return services.AddMySqlDistributedLock(
            () => new MySqlConnection(connectionString),
            configure);
    }
}
