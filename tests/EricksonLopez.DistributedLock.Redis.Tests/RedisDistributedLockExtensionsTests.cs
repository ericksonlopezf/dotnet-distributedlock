// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Redis;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using StackExchange.Redis;
using Xunit;

namespace EricksonLopez.DistributedLock.Redis.Tests;

public sealed class RedisDistributedLockExtensionsTests
{
    [Fact]
    public void AddRedisDistributedLock_WithMultiplexerAndConfigure_RegistersAndResolvesSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        var muxer = Substitute.For<IConnectionMultiplexer>();

        services.AddRedisDistributedLock(
            muxer,
            options =>
            {
                options.KeyPrefix = "test-custom:";
                options.DefaultExpiry = TimeSpan.FromSeconds(45);
                options.KeepaliveCadence = TimeSpan.FromSeconds(15);
                options.RetryInterval = TimeSpan.FromMilliseconds(120);
                options.BackoffJitter = false;
            });

        services.Should().Contain(sd => sd.ServiceType == typeof(IDistributedLockProvider) && sd.Lifetime == ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();
        var lockProvider = provider.GetRequiredService<IDistributedLockProvider>();
        lockProvider.Should().NotBeNull();
        lockProvider.Should().BeOfType<RedisDistributedLockProvider>();

        var options = provider.GetRequiredService<IOptions<RedisLockOptions>>().Value;
        options.KeyPrefix.Should().Be("test-custom:");
        options.DefaultExpiry.Should().Be(TimeSpan.FromSeconds(45));
        options.KeepaliveCadence.Should().Be(TimeSpan.FromSeconds(15));
        options.RetryInterval.Should().Be(TimeSpan.FromMilliseconds(120));
        options.BackoffJitter.Should().BeFalse();
    }

    [Fact]
    public void AddRedisDistributedLock_WithMultiplexerWithoutConfigure_RegistersAndResolvesSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        var muxer = Substitute.For<IConnectionMultiplexer>();

        services.AddRedisDistributedLock(muxer);

        services.Should().Contain(sd => sd.ServiceType == typeof(IDistributedLockProvider) && sd.Lifetime == ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();
        var lockProvider = provider.GetRequiredService<IDistributedLockProvider>();
        lockProvider.Should().NotBeNull();
        lockProvider.Should().BeOfType<RedisDistributedLockProvider>();
    }

    [Fact]
    public void AddRedisDistributedLock_FromDiWithConfigure_RegistersAndResolvesSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        var muxer = Substitute.For<IConnectionMultiplexer>();
        services.AddSingleton(muxer);

        services.AddRedisDistributedLock(options =>
        {
            options.KeyPrefix = "di-prefix:";
            options.DefaultExpiry = TimeSpan.FromMinutes(2);
        });

        services.Should().Contain(sd => sd.ServiceType == typeof(IDistributedLockProvider) && sd.Lifetime == ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();
        var lockProvider = provider.GetRequiredService<IDistributedLockProvider>();
        lockProvider.Should().NotBeNull();
        lockProvider.Should().BeOfType<RedisDistributedLockProvider>();

        var options = provider.GetRequiredService<IOptions<RedisLockOptions>>().Value;
        options.KeyPrefix.Should().Be("di-prefix:");
        options.DefaultExpiry.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void AddRedisDistributedLock_FromDiWithoutConfigure_RegistersAndResolvesSingleton()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        var muxer = Substitute.For<IConnectionMultiplexer>();
        services.AddSingleton(muxer);

        services.AddRedisDistributedLock();

        services.Should().Contain(sd => sd.ServiceType == typeof(IDistributedLockProvider) && sd.Lifetime == ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();
        var lockProvider = provider.GetRequiredService<IDistributedLockProvider>();
        lockProvider.Should().NotBeNull();
        lockProvider.Should().BeOfType<RedisDistributedLockProvider>();
    }

    [Fact]
    public void AddRedisDistributedLock_NullServicesWithMultiplexer_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        var muxer = Substitute.For<IConnectionMultiplexer>();
        var act = () => services.AddRedisDistributedLock(muxer);
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddRedisDistributedLock_NullServicesFromDi_ThrowsArgumentNullException()
    {
        IServiceCollection services = null!;
        var act = () => services.AddRedisDistributedLock();
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddRedisDistributedLock_NullMultiplexer_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        var act = () => services.AddRedisDistributedLock((IConnectionMultiplexer)null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("multiplexer");
    }
}
