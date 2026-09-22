// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data.Common;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Sqlite;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EricksonLopez.DistributedLock.Sqlite.Tests;

public sealed class SqliteDistributedLockExtensionsTests
{
    [Fact]
    public void AddSqliteDistributedLock_WithConnectionFactoryAndConfigure_RegistersAndResolvesSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddSqliteDistributedLock(
            () => new FakeDbConnection(),
            options =>
            {
                options.CommandTimeoutSeconds = 45;
                options.RetryInterval = TimeSpan.FromMilliseconds(120);
                options.BackoffJitter = false;
            });

        services.Should().Contain(sd => sd.ServiceType == typeof(IDistributedLockProvider) && sd.Lifetime == ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();
        var lockProvider = provider.GetRequiredService<IDistributedLockProvider>();
        lockProvider.Should().NotBeNull();
        lockProvider.Should().BeOfType<SqliteDistributedLockProvider>();

        var options = provider.GetRequiredService<IOptions<SqliteLockOptions>>().Value;
        options.CommandTimeoutSeconds.Should().Be(45);
        options.RetryInterval.Should().Be(TimeSpan.FromMilliseconds(120));
        options.BackoffJitter.Should().BeFalse();
    }

    [Fact]
    public void AddSqliteDistributedLock_WithConnectionFactoryWithoutConfigure_RegistersAndResolvesSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddSqliteDistributedLock(() => new FakeDbConnection());

        using var provider = services.BuildServiceProvider();
        var lockProvider = provider.GetRequiredService<IDistributedLockProvider>();
        lockProvider.Should().NotBeNull();
        lockProvider.Should().BeOfType<SqliteDistributedLockProvider>();
    }

    [Fact]
    public void AddSqliteDistributedLock_WithConnectionString_RegistersProviderAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddSqliteDistributedLock(
            "Data Source=test_ext.db",
            options =>
            {
                options.CommandTimeoutSeconds = 50;
            });

        services.Should().Contain(sd => sd.ServiceType == typeof(IDistributedLockProvider) && sd.Lifetime == ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();
        var lockProvider = provider.GetRequiredService<IDistributedLockProvider>();
        lockProvider.Should().NotBeNull();
    }

    [Fact]
    public void AddSqliteDistributedLock_WithConnectionStringWithoutConfigure_RegistersProviderAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddSqliteDistributedLock("Data Source=test_ext2.db");

        services.Should().Contain(sd => sd.ServiceType == typeof(IDistributedLockProvider) && sd.Lifetime == ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();
        var lockProvider = provider.GetRequiredService<IDistributedLockProvider>();
        lockProvider.Should().NotBeNull();
    }

    [Fact]
    public void AddSqliteDistributedLock_NullServicesWithFactory_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        var act = () => services.AddSqliteDistributedLock(() => new FakeDbConnection());
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddSqliteDistributedLock_NullServicesWithConnectionString_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        var act = () => services.AddSqliteDistributedLock("Data Source=test.db");
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddSqliteDistributedLock_NullConnectionFactory_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        var act = () => services.AddSqliteDistributedLock((Func<DbConnection>)null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddSqliteDistributedLock_InvalidConnectionString_ThrowsArgumentException(string? connStr)
    {
        var services = new ServiceCollection();
        var act = () => services.AddSqliteDistributedLock(connStr!);
        act.Should().Throw<ArgumentException>();
    }
}
