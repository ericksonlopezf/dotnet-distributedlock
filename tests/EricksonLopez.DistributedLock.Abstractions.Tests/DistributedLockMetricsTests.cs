// Copyright © Erickson Lopez. MIT License.
using System.Diagnostics.Metrics;
using EricksonLopez.DistributedLock.Abstractions;
using FluentAssertions;
using Xunit;

namespace EricksonLopez.DistributedLock.Abstractions.Tests;

public sealed class DistributedLockMetricsTests
{
    [Fact]
    public void Constants_ShouldExposeExpectedValues()
    {
        DistributedLockMetrics.MeterName.Should().Be("EricksonLopez.DistributedLock");
        DistributedLockMetrics.MeterVersion.Should().Be("1.0.0");
    }

    [Fact]
    public void RecordAcquisition_ShouldNotThrow_WhenInvoked()
    {
        var act = () => DistributedLockMetrics.RecordAcquisition("order-lock", "session", "acquired");

        act.Should().NotThrow();
    }

    [Fact]
    public void RecordWaitDuration_ShouldNotThrow_WhenInvoked()
    {
        var act = () => DistributedLockMetrics.RecordWaitDuration("order-lock", "session", "acquired", 12.5);

        act.Should().NotThrow();
    }

    [Fact]
    public void RecordHoldDuration_ShouldNotThrow_WhenInvoked()
    {
        var act = () => DistributedLockMetrics.RecordHoldDuration("order-lock", "session", 250.0);

        act.Should().NotThrow();
    }

    [Fact]
    public void RecordLockLost_ShouldNotThrow_WhenInvoked()
    {
        var act = () => DistributedLockMetrics.RecordLockLost("order-lock", 987654321);

        act.Should().NotThrow();
    }
}
