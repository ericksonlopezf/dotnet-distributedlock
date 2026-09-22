// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.DistributedLock.PostgreSql;
using FluentAssertions;
using Xunit;

namespace EricksonLopez.DistributedLock.PostgreSql.Tests;

public sealed class PostgresLockOptionsTests
{
    [Fact]
    public void PostgresLockOptions_HasCorrectDefaults()
    {
        var options = new PostgresLockOptions();

        options.KeepaliveCadence.Should().Be(TimeSpan.FromSeconds(30));
        options.InitialPollingInterval.Should().Be(TimeSpan.FromMilliseconds(25));
        options.MaxPollingInterval.Should().Be(TimeSpan.FromMilliseconds(250));
        options.JitterRatio.Should().Be(0.25);
        options.CommandTimeoutSeconds.Should().BeNull();
    }

    [Fact]
    public void PostgresLockOptions_AllowsCustomization()
    {
        var options = new PostgresLockOptions
        {
            KeepaliveCadence = TimeSpan.FromSeconds(45),
            InitialPollingInterval = TimeSpan.FromMilliseconds(50),
            MaxPollingInterval = TimeSpan.FromSeconds(1),
            JitterRatio = 0.1,
            CommandTimeoutSeconds = 60
        };

        options.KeepaliveCadence.Should().Be(TimeSpan.FromSeconds(45));
        options.InitialPollingInterval.Should().Be(TimeSpan.FromMilliseconds(50));
        options.MaxPollingInterval.Should().Be(TimeSpan.FromSeconds(1));
        options.JitterRatio.Should().Be(0.1);
        options.CommandTimeoutSeconds.Should().Be(60);
    }
}
