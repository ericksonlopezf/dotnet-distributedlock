// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data.Common;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Oracle;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EricksonLopez.DistributedLock.Oracle.Tests;

public sealed class OracleDistributedLockExtensionsTests
{
    [Fact]
    public void AddOracleDistributedLock_WithConnectionFactoryAndConfigure_RegistersAndResolvesSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddOracleDistributedLock(
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
        lockProvider.Should().BeOfType<OracleDistributedLockProvider>();

        var options = provider.GetRequiredService<IOptions<OracleLockOptions>>().Value;
        options.CommandTimeoutSeconds.Should().Be(45);
        options.KeepaliveCadence.Should().Be(TimeSpan.FromSeconds(25));
    }

    [Fact]
    public void AddOracleDistributedLock_WithConnectionFactoryWithoutConfigure_RegistersAndResolvesSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

        services.AddOracleDistributedLock(() => new FakeDbConnection());

        using var provider = services.BuildServiceProvider();
        var lockProvider = provider.GetRequiredService<IDistributedLockProvider>();
        lockProvider.Should().NotBeNull();
        lockProvider.Should().BeOfType<OracleDistributedLockProvider>();
    }

    [Fact]
    public void AddOracleDistributedLock_WithConnectionString_RegistersProviderAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddOracleDistributedLock(
            "Data Source=localhost:1521/XEPDB1;User Id=system;Password=oracle;",
            options =>
            {
                options.CommandTimeoutSeconds = 45;
            });

        services.Should().Contain(sd => sd.ServiceType == typeof(IDistributedLockProvider) && sd.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddOracleDistributedLock_WithConnectionStringWithoutConfigure_RegistersProviderAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddOracleDistributedLock("Data Source=localhost:1521/XEPDB1;User Id=system;Password=oracle;");

        services.Should().Contain(sd => sd.ServiceType == typeof(IDistributedLockProvider) && sd.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void AddOracleDistributedLock_NullServicesWithFactory_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        var act = () => services.AddOracleDistributedLock(() => new FakeDbConnection());
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddOracleDistributedLock_NullServicesWithConnectionString_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        var act = () => services.AddOracleDistributedLock("Data Source=localhost:1521/XEPDB1;User Id=system;Password=oracle;");
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddOracleDistributedLock_NullConnectionFactory_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        var act = () => services.AddOracleDistributedLock((Func<DbConnection>)null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AddOracleDistributedLock_InvalidConnectionString_ThrowsArgumentException(string? connectionString)
    {
        var services = new ServiceCollection();
        var act = () => services.AddOracleDistributedLock(connectionString!);
        act.Should().Throw<ArgumentException>();
    }
}
