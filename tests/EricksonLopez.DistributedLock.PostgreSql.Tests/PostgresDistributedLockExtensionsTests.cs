// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Data.Common;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.PostgreSql;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.DistributedLock.PostgreSql.Tests;

public sealed class PostgresDistributedLockExtensionsTests
{
    [Fact]
    public void AddPostgresDistributedLock_DbConnectionFactory_RegistersAndResolvesSingletonProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<PostgresDistributedLockProvider>>(NullLogger<PostgresDistributedLockProvider>.Instance);

        services.AddPostgresDistributedLock(
            sp => new FakeDbConnection(),
            options =>
            {
                options.KeepaliveCadence = TimeSpan.FromSeconds(10);
                options.JitterRatio = 0.5;
                options.CommandTimeoutSeconds = 45;
            });

        using var provider = services.BuildServiceProvider();
        var lockProvider = provider.GetService<IDistributedLockProvider>();

        lockProvider.Should().NotBeNull();
        lockProvider.Should().BeOfType<PostgresDistributedLockProvider>();

        var options = provider.GetRequiredService<IOptions<PostgresLockOptions>>().Value;
        options.KeepaliveCadence.Should().Be(TimeSpan.FromSeconds(10));
        options.JitterRatio.Should().Be(0.5);
        options.CommandTimeoutSeconds.Should().Be(45);

        var lockProvider2 = provider.GetService<IDistributedLockProvider>();
        lockProvider2.Should().BeSameAs(lockProvider);
    }

    [Fact]
    public void AddPostgresDistributedLock_DbConnectionFactory_WithoutConfigure_UsesDefaultOptions()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<PostgresDistributedLockProvider>>(NullLogger<PostgresDistributedLockProvider>.Instance);

        services.AddPostgresDistributedLock(sp => new FakeDbConnection());

        using var provider = services.BuildServiceProvider();
        var lockProvider = provider.GetService<IDistributedLockProvider>();

        lockProvider.Should().NotBeNull();
    }

    [Fact]
    public void AddPostgresDistributedLock_DbConnectionFactory_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        var act = () => services.AddPostgresDistributedLock(sp => new FakeDbConnection());
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddPostgresDistributedLock_DbConnectionFactory_NullFactory_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        Func<IServiceProvider, DbConnection> factory = null!;
        var act = () => services.AddPostgresDistributedLock(factory);
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void AddPostgresDistributedLock_IDbConnectionFactory_ReturningDbConnection_RegistersAndResolvesProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<PostgresDistributedLockProvider>>(NullLogger<PostgresDistributedLockProvider>.Instance);

        Func<IServiceProvider, IDbConnection> factory = sp => new FakeDbConnection();
        services.AddPostgresDistributedLock(factory, options => options.CommandTimeoutSeconds = 10);

        using var provider = services.BuildServiceProvider();
        var lockProvider = provider.GetService<IDistributedLockProvider>();

        lockProvider.Should().NotBeNull();
        lockProvider.Should().BeOfType<PostgresDistributedLockProvider>();

        var options = provider.GetRequiredService<IOptions<PostgresLockOptions>>().Value;
        options.CommandTimeoutSeconds.Should().Be(10);
    }

    [Fact]
    public void AddPostgresDistributedLock_IDbConnectionFactory_ReturningSyncConnection_RegistersAndResolvesProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<PostgresDistributedLockProvider>>(NullLogger<PostgresDistributedLockProvider>.Instance);

        Func<IServiceProvider, IDbConnection> factory = sp => new FakeSyncConnection();
        services.AddPostgresDistributedLock(factory);

        using var provider = services.BuildServiceProvider();
        var lockProvider = provider.GetService<IDistributedLockProvider>();

        lockProvider.Should().NotBeNull();
        lockProvider.Should().BeOfType<PostgresDistributedLockProvider>();
    }

    [Fact]
    public void AddPostgresDistributedLock_IDbConnectionFactory_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        Func<IServiceProvider, IDbConnection> factory = sp => new FakeSyncConnection();
        var act = () => services.AddPostgresDistributedLock(factory);
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddPostgresDistributedLock_IDbConnectionFactory_NullFactory_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        Func<IServiceProvider, IDbConnection> factory = null!;
        var act = () => services.AddPostgresDistributedLock(factory);
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }
}
