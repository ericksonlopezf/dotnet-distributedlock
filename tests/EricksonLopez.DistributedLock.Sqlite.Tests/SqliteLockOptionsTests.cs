// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.DistributedLock.Sqlite;
using FluentAssertions;
using Xunit;

namespace EricksonLopez.DistributedLock.Sqlite.Tests;

public sealed class SqliteLockOptionsTests
{
    [Fact]
    public void SqliteLockOptions_HasSensibleDefaults()
    {
        var options = new SqliteLockOptions();

        options.CommandTimeoutSeconds.Should().Be(30);
        options.RetryInterval.Should().Be(TimeSpan.FromMilliseconds(50));
        options.BackoffJitter.Should().BeTrue();
    }

    [Fact]
    public void SqliteLockOptions_CanBeCustomized()
    {
        var options = new SqliteLockOptions
        {
            CommandTimeoutSeconds = 45,
            RetryInterval = TimeSpan.FromMilliseconds(100),
            BackoffJitter = false
        };

        options.CommandTimeoutSeconds.Should().Be(45);
        options.RetryInterval.Should().Be(TimeSpan.FromMilliseconds(100));
        options.BackoffJitter.Should().BeFalse();
    }
}
