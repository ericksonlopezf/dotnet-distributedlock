// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data.Common;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.SqlServer;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.DistributedLock.SqlServer.Tests;

public sealed class SqlServerDistributedLockExtensionsTests
{
    [Fact]
    public void AddSqlServerDistributedLock_WithConnectionFactoryAndConfigure_RegistersAndResolvesSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddSqlServerDistributedLock(
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
        lockProvider.Should().BeOfType<SqlServerDistributedLockProvider>();

        var options = provider.GetRequiredService<IOptions<SqlServerLockOptions>>().Value;
        options.CommandTimeoutSeconds.Should().Be(45);
        options.KeepaliveCadence.Should().Be(TimeSpan.FromSeconds(25));
    }

    [Fact]
    public void AddSqlServerDistributedLock_WithConnectionFactoryWithoutConfigure_RegistersAndResolvesSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddSqlServerDistributedLock(() => new FakeDbConnection());

        using var provider = services.BuildServiceProvider();
        var lockProvider = provider.GetRequiredService<IDistributedLockProvider>();
        lockProvider.Should().NotBeNull();
        lockProvider.Should().BeOfType<SqlServerDistributedLockProvider>();
    }

    [Fact]
    public void AddSqlServerDistributedLock_WithConnectionString_RegistersProviderAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddSqlServerDistributedLock(
            "Server=localhost;Database=test;User Id=sa;Password=Pass;",
            options =>
            {
                options.CommandTimeoutSeconds = 55;
            });

        services.Should().Contain(sd => sd.ServiceType == typeof(IDistributedLockProvider) && sd.Lifetime == ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();
        var lockProvider = provider.GetRequiredService<IDistributedLockProvider>();
        lockProvider.Should().NotBeNull();
        lockProvider.Should().BeOfType<SqlServerDistributedLockProvider>();

        // Verify the factory creates a SqlConnection without opening it
        var factoryField = typeof(SqlServerDistributedLockProvider).GetField("_connectionFactory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        factoryField.Should().NotBeNull();
        var factory = (Func<DbConnection>)factoryField!.GetValue(lockProvider)!;
        using var createdConn = factory();
        createdConn.Should().NotBeNull();
        createdConn.ConnectionString.Should().Contain("localhost");
    }

    [Fact]
    public void AddSqlServerDistributedLock_WithConnectionStringWithoutConfigure_RegistersProviderAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddSqlServerDistributedLock("Server=localhost;Database=test;User Id=sa;Password=Pass;");

        services.Should().Contain(sd => sd.ServiceType == typeof(IDistributedLockProvider) && sd.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddSqlServerDistributedLock_NullServicesWithFactory_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        var act = () => services.AddSqlServerDistributedLock(() => new FakeDbConnection());
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddSqlServerDistributedLock_NullServicesWithConnectionString_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        var act = () => services.AddSqlServerDistributedLock("Server=localhost;Database=test;");
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddSqlServerDistributedLock_NullConnectionFactory_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        var act = () => services.AddSqlServerDistributedLock((Func<DbConnection>)null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AddSqlServerDistributedLock_InvalidConnectionString_ThrowsArgumentException(string? connectionString)
    {
        var services = new ServiceCollection();
        var act = () => services.AddSqlServerDistributedLock(connectionString!);
        act.Should().Throw<ArgumentException>();
    }
}
