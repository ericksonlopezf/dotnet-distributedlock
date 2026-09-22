// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.DistributedLock.MySql;
using FluentAssertions;
using Xunit;

namespace EricksonLopez.DistributedLock.MySql.Tests;

public sealed class MySqlLockOptionsTests
{
    [Fact]
    public void MySqlLockOptions_HasSensibleDefaults()
    {
        var options = new MySqlLockOptions();

        options.CommandTimeoutSeconds.Should().Be(30);
        options.KeepaliveCadence.Should().Be(TimeSpan.FromSeconds(10));
        options.RetryInterval.Should().Be(TimeSpan.FromMilliseconds(50));
        options.BackoffJitter.Should().BeTrue();
    }

    [Fact]
    public void MySqlLockOptions_CanBeCustomized()
    {
        var options = new MySqlLockOptions
        {
            CommandTimeoutSeconds = 45,
            KeepaliveCadence = TimeSpan.FromSeconds(25),
            RetryInterval = TimeSpan.FromMilliseconds(100),
            BackoffJitter = false
        };

        options.CommandTimeoutSeconds.Should().Be(45);
        options.KeepaliveCadence.Should().Be(TimeSpan.FromSeconds(25));
        options.RetryInterval.Should().Be(TimeSpan.FromMilliseconds(100));
        options.BackoffJitter.Should().BeFalse();
    }
}
