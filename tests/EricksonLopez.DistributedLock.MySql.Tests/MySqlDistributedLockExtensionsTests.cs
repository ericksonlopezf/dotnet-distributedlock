// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data.Common;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.MySql;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EricksonLopez.DistributedLock.MySql.Tests;

public sealed class MySqlDistributedLockExtensionsTests
{
    [Fact]
    public void AddMySqlDistributedLock_WithConnectionFactoryAndConfigure_RegistersAndResolvesSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddMySqlDistributedLock(
            () => new FakeDbConnection(),
            options =>
            {
                options.CommandTimeoutSeconds = 45;
                options.KeepaliveCadence = TimeSpan.FromSeconds(25);
            });

        services.Should().Contain(sd => sd.ServiceType == typeof(IDistributedLockProvider) && sd.Lifetime == ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();
        var lockProvider = provider.GetRequiredService<IDistributedLockProvider>();
        lockProvider.Should().NotBeNull();
        lockProvider.Should().BeOfType<MySqlDistributedLockProvider>();

        var options = provider.GetRequiredService<IOptions<MySqlLockOptions>>().Value;
        options.CommandTimeoutSeconds.Should().Be(45);
        options.KeepaliveCadence.Should().Be(TimeSpan.FromSeconds(25));
    }

    [Fact]
    public void AddMySqlDistributedLock_WithConnectionFactoryWithoutConfigure_RegistersAndResolvesSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddMySqlDistributedLock(() => new FakeDbConnection());

        using var provider = services.BuildServiceProvider();
        var lockProvider = provider.GetRequiredService<IDistributedLockProvider>();
        lockProvider.Should().NotBeNull();
        lockProvider.Should().BeOfType<MySqlDistributedLockProvider>();
    }

    [Fact]
    public void AddMySqlDistributedLock_WithConnectionString_RegistersProviderAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddMySqlDistributedLock(
            "Server=localhost;Database=test;User Id=root;Password=Pass;",
            options =>
            {
                options.CommandTimeoutSeconds = 55;
            });

        services.Should().Contain(sd => sd.ServiceType == typeof(IDistributedLockProvider) && sd.Lifetime == ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();
        var lockProvider = provider.GetRequiredService<IDistributedLockProvider>();
        lockProvider.Should().NotBeNull();
        lockProvider.Should().BeOfType<MySqlDistributedLockProvider>();

        var factoryField = typeof(MySqlDistributedLockProvider).GetField("_connectionFactory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        factoryField.Should().NotBeNull();
        var factory = (Func<DbConnection>)factoryField!.GetValue(lockProvider)!;
        using var createdConn = factory();
        createdConn.Should().NotBeNull();
        createdConn.ConnectionString.Should().Contain("localhost");
    }

    [Fact]
    public void AddMySqlDistributedLock_WithConnectionStringWithoutConfigure_RegistersProviderAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddMySqlDistributedLock("Server=localhost;Database=test;User Id=root;Password=Pass;");

        services.Should().Contain(sd => sd.ServiceType == typeof(IDistributedLockProvider) && sd.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddMySqlDistributedLock_NullServicesWithFactory_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        var act = () => services.AddMySqlDistributedLock(() => new FakeDbConnection());
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddMySqlDistributedLock_NullServicesWithConnectionString_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        var act = () => services.AddMySqlDistributedLock("Server=localhost;Database=test;");
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddMySqlDistributedLock_NullConnectionFactory_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        var act = () => services.AddMySqlDistributedLock((Func<DbConnection>)null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AddMySqlDistributedLock_InvalidConnectionString_ThrowsArgumentException(string? connectionString)
    {
        var services = new ServiceCollection();
        var act = () => services.AddMySqlDistributedLock(connectionString!);
        act.Should().Throw<ArgumentException>();
    }
}
