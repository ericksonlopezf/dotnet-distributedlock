// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Data.Common;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.PostgreSql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Provides extension methods for registering PostgreSQL distributed lock services in an <see cref="IServiceCollection"/>.
/// </summary>
public static class PostgresDistributedLockExtensions
{
    /// <summary>
    /// Registers the PostgreSQL distributed lock provider with a <see cref="DbConnection"/> factory delegate.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="connectionFactory">The factory delegate used to instantiate dedicated database connections.</param>
    /// <param name="configure">The optional configuration action for <see cref="PostgresLockOptions"/>.</param>
    /// <returns>The configured service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="connectionFactory"/> is <see langword="null"/></exception>
    public static IServiceCollection AddPostgresDistributedLock(
        this IServiceCollection services,
        Func<IServiceProvider, DbConnection> connectionFactory,
        Action<PostgresLockOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionFactory);

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddSingleton<IDistributedLockProvider>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<PostgresDistributedLockProvider>>();
            var options = sp.GetService<IOptions<PostgresLockOptions>>()?.Value ?? new PostgresLockOptions();
            return new PostgresDistributedLockProvider(() => connectionFactory(sp), logger, options);
        });

        return services;
    }

    /// <summary>
    /// Registers the PostgreSQL distributed lock provider with an <see cref="IDbConnection"/> factory delegate.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="connectionFactory">The factory delegate used to instantiate database connections.</param>
    /// <param name="configure">The optional configuration action for <see cref="PostgresLockOptions"/>.</param>
    /// <returns>The configured service collection.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="connectionFactory"/> is <see langword="null"/></exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown at runtime (during the first lock acquisition) when <paramref name="connectionFactory"/> produces a connection
    /// that is not an instance of <see cref="DbConnection"/>. The PostgreSQL provider requires a concrete
    /// <see cref="DbConnection"/> subclass (e.g., <c>NpgsqlConnection</c>) for command execution.
    /// </exception>
    public static IServiceCollection AddPostgresDistributedLock(
        this IServiceCollection services,
        Func<IServiceProvider, IDbConnection> connectionFactory,
        Action<PostgresLockOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionFactory);

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.AddSingleton<IDistributedLockProvider>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<PostgresDistributedLockProvider>>();
            var options = sp.GetService<IOptions<PostgresLockOptions>>()?.Value ?? new PostgresLockOptions();

            return new PostgresDistributedLockProvider(
                () =>
                {
                    var connection = connectionFactory(sp);
                    if (connection is DbConnection dbConn)
                    {
                        return dbConn;
                    }

                    throw new InvalidOperationException($"The connection factory must produce an instance of {nameof(DbConnection)} for PostgreSQL distributed locks.");
                },
                logger,
                options);
        });

        return services;
    }
}
