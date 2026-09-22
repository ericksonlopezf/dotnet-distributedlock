// Copyright © Erickson Lopez. MIT License.
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using EricksonLopez.DistributedLock.Abstractions;
using FluentAssertions;
using Xunit;

namespace EricksonLopez.DistributedLock.PostgreSql.Tests;

[Collection("NonParallelMetricsCollection")]
public sealed class DistributedLockMetricsTests
{
    [Fact]
    public void Metrics_HaveValidMeterNameAndVersion()
    {
        DistributedLockMetrics.MeterName.Should().Be("EricksonLopez.DistributedLock");
        DistributedLockMetrics.MeterVersion.Should().Be("1.0.0");
    }

    [Fact]
    public void RecordAcquisition_RecordsCounterWithCorrectTags()
    {
        using var listener = new MeterListener();
        var captured = new ConcurrentBag<(string Instrument, long Measurement, Dictionary<string, object?> Tags)>();

        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == DistributedLockMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            var dict = new Dictionary<string, object?>();
            foreach (var tag in tags)
            {
                dict[tag.Key] = tag.Value;
            }
            captured.Add((instrument.Name, measurement, dict));
        });

        listener.Start();

        DistributedLockMetrics.RecordAcquisition("metrics-order-100", "session", "acquired");

        var record = captured.Should().Contain(c => c.Instrument == "distributed_lock.acquisitions" && (string?)c.Tags["resource_id"] == "metrics-order-100").Which;
        record.Measurement.Should().Be(1);
        record.Tags["lock_type"].Should().Be("session");
        record.Tags["status"].Should().Be("acquired");
    }

    [Fact]
    public void RecordWaitDuration_RecordsHistogramWithCorrectTags()
    {
        using var listener = new MeterListener();
        var captured = new ConcurrentBag<(string Instrument, double Measurement, Dictionary<string, object?> Tags)>();

        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == DistributedLockMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            var dict = new Dictionary<string, object?>();
            foreach (var tag in tags)
            {
                dict[tag.Key] = tag.Value;
            }
            captured.Add((instrument.Name, measurement, dict));
        });

        listener.Start();

        DistributedLockMetrics.RecordWaitDuration("metrics-order-200", "transaction", "timeout", 45.5);

        var record = captured.Should().Contain(c => c.Instrument == "distributed_lock.wait_duration" && (string?)c.Tags["resource_id"] == "metrics-order-200").Which;
        record.Measurement.Should().Be(45.5);
        record.Tags["lock_type"].Should().Be("transaction");
        record.Tags["status"].Should().Be("timeout");
    }

    [Fact]
    public void RecordHoldDuration_RecordsHistogramWithCorrectTags()
    {
        using var listener = new MeterListener();
        var captured = new ConcurrentBag<(string Instrument, double Measurement, Dictionary<string, object?> Tags)>();

        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == DistributedLockMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            var dict = new Dictionary<string, object?>();
            foreach (var tag in tags)
            {
                dict[tag.Key] = tag.Value;
            }
            captured.Add((instrument.Name, measurement, dict));
        });

        listener.Start();

        DistributedLockMetrics.RecordHoldDuration("metrics-order-300", "session", 120.0);

        var record = captured.Should().Contain(c => c.Instrument == "distributed_lock.hold_duration" && (string?)c.Tags["resource_id"] == "metrics-order-300").Which;
        record.Measurement.Should().Be(120.0);
        record.Tags["lock_type"].Should().Be("session");
    }

    [Fact]
    public void RecordLockLost_RecordsCounterWithCorrectTags()
    {
        using var listener = new MeterListener();
        var captured = new ConcurrentBag<(string Instrument, long Measurement, Dictionary<string, object?> Tags)>();

        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == DistributedLockMetrics.MeterName)
            {
                l.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            var dict = new Dictionary<string, object?>();
            foreach (var tag in tags)
            {
                dict[tag.Key] = tag.Value;
            }
            captured.Add((instrument.Name, measurement, dict));
        });

        listener.Start();

        DistributedLockMetrics.RecordLockLost("metrics-order-400", 99999);

        var record = captured.Should().Contain(c => c.Instrument == "distributed_lock.lost" && (string?)c.Tags["resource_id"] == "metrics-order-400").Which;
        record.Measurement.Should().Be(1);
        record.Tags["lock_id"].Should().Be(99999L);
    }
}
