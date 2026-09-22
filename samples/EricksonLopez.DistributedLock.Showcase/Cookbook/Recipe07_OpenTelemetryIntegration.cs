// Copyright © Erickson Lopez. MIT License.
using System;
using System.Diagnostics.Metrics;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Showcase.Infrastructure;

namespace EricksonLopez.DistributedLock.Showcase.Cookbook;

/// <summary>
/// Recipe 7: Real-time observability integration with OpenTelemetry.
/// Problem: Monitor acquisition rates, contention times (wait duration), and lock hold duration.
/// </summary>
public static class Recipe07_OpenTelemetryIntegration
{
    public static Task RunAsync()
    {
        ConsoleUi.PrintHeader("Recipe 07: OpenTelemetry Observability",
            "Capturing metrics with MeterListener for DistributedLockMetrics.");

        // Configure local MeterListener to observe official metrics
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == DistributedLockMetrics.MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            ConsoleUi.PrintMetric($"[OTel Event] {instrument.Name}", measurement, instrument.Unit);
        });

        meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            ConsoleUi.PrintMetric($"[OTel Histogram] {instrument.Name}", measurement, instrument.Unit);
        });

        meterListener.Start();
        ConsoleUi.PrintSuccess($"MeterListener subscribed to '{DistributedLockMetrics.MeterName}'.");

        // Emit demonstration metrics
        DistributedLockMetrics.RecordAcquisition("orders:report:annual", "session", "acquired");
        DistributedLockMetrics.RecordWaitDuration("orders:report:annual", "session", "acquired", 15.3);
        DistributedLockMetrics.RecordHoldDuration("orders:report:annual", "session", 320.0);

        meterListener.RecordObservableInstruments();
        return Task.CompletedTask;
    }
}
