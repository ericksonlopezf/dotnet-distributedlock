// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.DistributedLock.SqlServer;
using FluentAssertions;
using Xunit;

namespace EricksonLopez.DistributedLock.SqlServer.Tests;

public sealed class SqlServerLockOptionsTests
{
    [Fact]
    public void SqlServerLockOptions_HasSensibleDefaults()
    {
        var options = new SqlServerLockOptions();

        options.CommandTimeoutSeconds.Should().Be(30);
        options.KeepaliveCadence.Should().Be(TimeSpan.FromSeconds(10));
        options.RetryInterval.Should().Be(TimeSpan.FromMilliseconds(50));
        options.BackoffJitter.Should().BeTrue();
    }

    [Fact]
    public void SqlServerLockOptions_CanBeCustomized()
    {
        var options = new SqlServerLockOptions
        {
            CommandTimeoutSeconds = 60,
            KeepaliveCadence = TimeSpan.FromSeconds(20),
            RetryInterval = TimeSpan.FromMilliseconds(100),
            BackoffJitter = false
        };

        options.CommandTimeoutSeconds.Should().Be(60);
        options.KeepaliveCadence.Should().Be(TimeSpan.FromSeconds(20));
        options.RetryInterval.Should().Be(TimeSpan.FromMilliseconds(100));
        options.BackoffJitter.Should().BeFalse();
    }
}
