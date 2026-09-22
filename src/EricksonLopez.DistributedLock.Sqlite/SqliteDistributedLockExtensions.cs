// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data.Common;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides extension methods for registering SQLite distributed lock services with an <see cref="IServiceCollection"/>.
/// </summary>
public static class SqliteDistributedLockExtensions
{
    /// <summary>
    /// Registers SQLite distributed lock services using the specified connection factory delegate.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="connectionFactory">The factory delegate that creates new database connections.</param>
    /// <param name="configure">An optional delegate to configure <see cref="SqliteLockOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance so that multiple calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="connectionFactory"/> is <see langword="null"/></exception>
    public static IServiceCollection AddSqliteDistributedLock(
        this IServiceCollection services,
        Func<DbConnection> connectionFactory,
        Action<SqliteLockOptions>? configure = null)
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
            var logger = sp.GetRequiredService<ILogger<SqliteDistributedLockProvider>>();
            var options = sp.GetRequiredService<IOptions<SqliteLockOptions>>();
            return new SqliteDistributedLockProvider(connectionFactory, logger, options);
        });

        return services;
    }

    /// <summary>
    /// Registers SQLite distributed lock services using the specified connection string.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="connectionString">The connection string used to create SQLite connections.</param>
    /// <param name="configure">An optional delegate to configure <see cref="SqliteLockOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance so that multiple calls can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/></exception>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> is empty or contains only white space</exception>
    public static IServiceCollection AddSqliteDistributedLock(
        this IServiceCollection services,
        string connectionString,
        Action<SqliteLockOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return services.AddSqliteDistributedLock(
            () => new SqliteConnection(connectionString),
            configure);
    }
}
