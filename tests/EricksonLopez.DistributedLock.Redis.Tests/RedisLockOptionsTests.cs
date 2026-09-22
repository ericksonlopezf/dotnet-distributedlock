// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.DistributedLock.Redis;
using FluentAssertions;
using Xunit;

namespace EricksonLopez.DistributedLock.Redis.Tests;

public sealed class RedisLockOptionsTests
{
    [Fact]
    public void RedisLockOptions_HasSensibleDefaults()
    {
        var options = new RedisLockOptions();

        options.DefaultExpiry.Should().Be(TimeSpan.FromSeconds(30));
        options.KeyPrefix.Should().Be("lock:");
        options.KeepaliveCadence.Should().Be(TimeSpan.FromSeconds(10));
        options.RetryInterval.Should().Be(TimeSpan.FromMilliseconds(50));
        options.BackoffJitter.Should().BeTrue();
    }

    [Fact]
    public void RedisLockOptions_CanBeCustomized()
    {
        var options = new RedisLockOptions
        {
            DefaultExpiry = TimeSpan.FromSeconds(60),
            KeyPrefix = "app:locks:",
            KeepaliveCadence = TimeSpan.FromSeconds(20),
            RetryInterval = TimeSpan.FromMilliseconds(100),
            BackoffJitter = false
        };

        options.DefaultExpiry.Should().Be(TimeSpan.FromSeconds(60));
        options.KeyPrefix.Should().Be("app:locks:");
        options.KeepaliveCadence.Should().Be(TimeSpan.FromSeconds(20));
        options.RetryInterval.Should().Be(TimeSpan.FromMilliseconds(100));
        options.BackoffJitter.Should().BeFalse();
    }
}
