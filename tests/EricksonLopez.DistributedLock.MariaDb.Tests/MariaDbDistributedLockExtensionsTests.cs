// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data.Common;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.MariaDb;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EricksonLopez.DistributedLock.MariaDb.Tests;

public sealed class MariaDbDistributedLockExtensionsTests
{
    [Fact]
    public void AddMariaDbDistributedLock_WithConnectionFactoryAndConfigure_RegistersAndResolvesSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddMariaDbDistributedLock(
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
        lockProvider.Should().BeOfType<MariaDbDistributedLockProvider>();

        var options = provider.GetRequiredService<IOptions<MariaDbLockOptions>>().Value;
        options.CommandTimeoutSeconds.Should().Be(45);
        options.KeepaliveCadence.Should().Be(TimeSpan.FromSeconds(25));
    }

    [Fact]
    public void AddMariaDbDistributedLock_WithConnectionFactoryWithoutConfigure_RegistersAndResolvesSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddMariaDbDistributedLock(() => new FakeDbConnection());

        using var provider = services.BuildServiceProvider();
        var lockProvider = provider.GetRequiredService<IDistributedLockProvider>();
        lockProvider.Should().NotBeNull();
        lockProvider.Should().BeOfType<MariaDbDistributedLockProvider>();
    }

    [Fact]
    public void AddMariaDbDistributedLock_WithConnectionString_RegistersProviderAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddMariaDbDistributedLock(
            "Server=localhost;Database=test;User Id=root;Password=Pass;",
            options =>
            {
                options.CommandTimeoutSeconds = 55;
            });

        services.Should().Contain(sd => sd.ServiceType == typeof(IDistributedLockProvider) && sd.Lifetime == ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();
        var lockProvider = provider.GetRequiredService<IDistributedLockProvider>();
        lockProvider.Should().NotBeNull();
        lockProvider.Should().BeOfType<MariaDbDistributedLockProvider>();

        var factoryField = typeof(MariaDbDistributedLockProvider).GetField("_connectionFactory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        factoryField.Should().NotBeNull();
        var factory = (Func<DbConnection>)factoryField!.GetValue(lockProvider)!;
        using var createdConn = factory();
        createdConn.Should().NotBeNull();
        createdConn.ConnectionString.Should().Contain("localhost");
    }

    [Fact]
    public void AddMariaDbDistributedLock_WithConnectionStringWithoutConfigure_RegistersProviderAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddMariaDbDistributedLock("Server=localhost;Database=test;User Id=root;Password=Pass;");

        services.Should().Contain(sd => sd.ServiceType == typeof(IDistributedLockProvider) && sd.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddMariaDbDistributedLock_NullServicesWithFactory_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        var act = () => services.AddMariaDbDistributedLock(() => new FakeDbConnection());
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddMariaDbDistributedLock_NullServicesWithConnectionString_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        var act = () => services.AddMariaDbDistributedLock("Server=localhost;Database=test;");
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddMariaDbDistributedLock_NullConnectionFactory_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        var act = () => services.AddMariaDbDistributedLock((Func<DbConnection>)null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AddMariaDbDistributedLock_InvalidConnectionString_ThrowsArgumentException(string? connectionString)
    {
        var services = new ServiceCollection();
        var act = () => services.AddMariaDbDistributedLock(connectionString!);
        act.Should().Throw<ArgumentException>();
    }
}
