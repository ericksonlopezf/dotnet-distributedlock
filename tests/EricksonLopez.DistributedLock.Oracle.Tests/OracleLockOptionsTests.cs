// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.DistributedLock.Oracle;
using FluentAssertions;
using Xunit;

namespace EricksonLopez.DistributedLock.Oracle.Tests;

public sealed class OracleLockOptionsTests
{
    [Fact]
    public void OracleLockOptions_HasSensibleDefaults()
    {
        var options = new OracleLockOptions();

        options.CommandTimeoutSeconds.Should().Be(30);
        options.KeepaliveCadence.Should().Be(TimeSpan.FromSeconds(10));
        options.RetryInterval.Should().Be(TimeSpan.FromMilliseconds(50));
        options.BackoffJitter.Should().BeTrue();
    }

    [Fact]
    public void OracleLockOptions_CanBeCustomized()
    {
        var options = new OracleLockOptions
        {
            CommandTimeoutSeconds = 45,
            KeepaliveCadence = TimeSpan.FromSeconds(20),
            RetryInterval = TimeSpan.FromMilliseconds(100),
            BackoffJitter = false
        };

        options.CommandTimeoutSeconds.Should().Be(45);
        options.KeepaliveCadence.Should().Be(TimeSpan.FromSeconds(20));
        options.RetryInterval.Should().Be(TimeSpan.FromMilliseconds(100));
        options.BackoffJitter.Should().BeFalse();
    }
}
