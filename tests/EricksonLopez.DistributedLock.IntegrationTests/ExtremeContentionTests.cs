// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.DistributedLock.Abstractions;
using EricksonLopez.DistributedLock.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EricksonLopez.DistributedLock.IntegrationTests;

[Trait("Category", "Contention")]
public sealed class ExtremeContentionTests : IDisposable
{
    private readonly string _dbPath;
    private readonly string _connectionString;

    public ExtremeContentionTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"contention_{Guid.NewGuid():N}.db");
        _connectionString = $"Data Source={_dbPath};Cache=Shared;Mode=ReadWriteCreate;";
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch
        {
            // Ignore temp cleanup errors
        }
    }

    [Fact]
    public async Task RunContentionScenarios_RecordMetrics()
    {
        int[] contenderCounts = [1, 2, 10, 50, 100, 500, 1000];
        var results = new List<ContentionMetricsRecord>();

        // Pre-initialize table once
        {
            await using var initConn = new SqliteConnection(_connectionString);
            await initConn.OpenAsync();
            await using var cmd = initConn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            await cmd.ExecuteNonQueryAsync();
            var provider = new SqliteDistributedLockProvider(() => new SqliteConnection(_connectionString), NullLogger<SqliteDistributedLockProvider>.Instance);
            var initLock = await provider.TryAcquireAsync("init-lock");
            if (initLock.IsSuccess) await initLock.Value.DisposeAsync();
        }

        foreach (var count in contenderCounts)
        {
            var record = await MeasureContentionAsync(count);
            results.Add(record);
        }

        var sb = new StringBuilder();
        sb.AppendLine("[");
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            sb.AppendLine("  {");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    \"Contenders\": {r.Contenders},");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    \"TotalDurationMs\": {r.TotalDurationMs:F2},");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    \"ThroughputOpsPerSec\": {r.ThroughputOpsPerSec:F2},");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    \"SuccessCount\": {r.SuccessCount},");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    \"FailedCount\": {r.FailedCount},");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    \"TimeoutCount\": {r.TimeoutCount},");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    \"MeanAcquisitionMs\": {r.MeanAcquisitionMs:F2},");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    \"P50AcquisitionMs\": {r.P50AcquisitionMs:F2},");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    \"P95AcquisitionMs\": {r.P95AcquisitionMs:F2},");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    \"P99AcquisitionMs\": {r.P99AcquisitionMs:F2},");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    \"MeanReleaseMs\": {r.MeanReleaseMs:F2},");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    \"AllocatedMemoryDeltaBytes\": {r.AllocatedMemoryDeltaBytes},");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    \"Gen0Collections\": {r.Gen0Collections},");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    \"Gen1Collections\": {r.Gen1Collections},");
            sb.AppendLine(CultureInfo.InvariantCulture, $"    \"Gen2Collections\": {r.Gen2Collections}");
            sb.Append("  }");
            if (i < results.Count - 1) sb.Append(',');
            sb.AppendLine();
        }
        sb.AppendLine("]");

        string targetDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "MEGA-AUDITORIA", "artifacts", "concurrency");
        Directory.CreateDirectory(targetDir);
        await File.WriteAllTextAsync(Path.Combine(targetDir, "contention_results.json"), sb.ToString());
    }

    private async Task<ContentionMetricsRecord> MeasureContentionAsync(int contenderCount)
    {
        const string resourceId = "extreme-contention-resource";
        var options = new SqliteLockOptions
        {
            CommandTimeoutSeconds = 15,
            RetryInterval = TimeSpan.FromMilliseconds(5),
            BackoffJitter = true
        };

        var provider = new SqliteDistributedLockProvider(
            () => new SqliteConnection(_connectionString),
            NullLogger<SqliteDistributedLockProvider>.Instance,
            options);

        var acquisitionLatencies = new ConcurrentBag<double>();
        var releaseLatencies = new ConcurrentBag<double>();
        int successCount = 0;
        int failureCount = 0;
        int timeoutCount = 0;

        int gen0Start = GC.CollectionCount(0);
        int gen1Start = GC.CollectionCount(1);
        int gen2Start = GC.CollectionCount(2);
        long memStart = GC.GetTotalMemory(true);

        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int readyCount = 0;
        var totalSw = Stopwatch.StartNew();

        var tasks = Enumerable.Range(0, contenderCount).Select(async _ =>
        {
            Interlocked.Increment(ref readyCount);
            await startGate.Task;

            var acqSw = Stopwatch.StartNew();
            // Bounded wait of 500ms to measure real timeout & contention under scale
            var result = await provider.TryAcquireAsync(resourceId, TimeSpan.FromMilliseconds(500));
            acqSw.Stop();
            acquisitionLatencies.Add(acqSw.Elapsed.TotalMilliseconds);

            if (result.IsSuccess)
            {
                Interlocked.Increment(ref successCount);
                // Simulate critical section
                await Task.Delay(2);

                var relSw = Stopwatch.StartNew();
                await result.Value.DisposeAsync();
                relSw.Stop();
                releaseLatencies.Add(relSw.Elapsed.TotalMilliseconds);
            }
            else
            {
                if (result.Error.Code == DistributedLockErrors.Timeout.Code)
                {
                    Interlocked.Increment(ref timeoutCount);
                }
                else
                {
                    Interlocked.Increment(ref failureCount);
                }
            }
        }).ToArray();

        while (Volatile.Read(ref readyCount) < contenderCount)
        {
            await Task.Yield();
        }
        startGate.SetResult();

        await Task.WhenAll(tasks);
        totalSw.Stop();

        long memEnd = GC.GetTotalMemory(false);
        int gen0Diff = GC.CollectionCount(0) - gen0Start;
        int gen1Diff = GC.CollectionCount(1) - gen1Start;
        int gen2Diff = GC.CollectionCount(2) - gen2Start;

        var sortedAcq = acquisitionLatencies.OrderBy(x => x).ToList();
        double p50 = sortedAcq.Count > 0 ? sortedAcq[(int)(sortedAcq.Count * 0.50)] : 0;
        double p95 = sortedAcq.Count > 0 ? sortedAcq[(int)(sortedAcq.Count * 0.95)] : 0;
        double p99 = sortedAcq.Count > 0 ? sortedAcq[(int)(sortedAcq.Count * 0.99)] : 0;
        double meanAcq = sortedAcq.Count > 0 ? sortedAcq.Average() : 0;
        double meanRel = !releaseLatencies.IsEmpty ? releaseLatencies.Average() : 0;
        double throughputOpsPerSec = (successCount + failureCount + timeoutCount) / (totalSw.Elapsed.TotalSeconds > 0 ? totalSw.Elapsed.TotalSeconds : 1.0);

        return new ContentionMetricsRecord
        {
            Contenders = contenderCount,
            TotalDurationMs = totalSw.Elapsed.TotalMilliseconds,
            ThroughputOpsPerSec = throughputOpsPerSec,
            SuccessCount = successCount,
            FailedCount = failureCount,
            TimeoutCount = timeoutCount,
            MeanAcquisitionMs = meanAcq,
            P50AcquisitionMs = p50,
            P95AcquisitionMs = p95,
            P99AcquisitionMs = p99,
            MeanReleaseMs = meanRel,
            AllocatedMemoryDeltaBytes = Math.Max(0, memEnd - memStart),
            Gen0Collections = gen0Diff,
            Gen1Collections = gen1Diff,
            Gen2Collections = gen2Diff
        };
    }

    public sealed class ContentionMetricsRecord
    {
        public int Contenders { get; set; }
        public double TotalDurationMs { get; set; }
        public double ThroughputOpsPerSec { get; set; }
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public int TimeoutCount { get; set; }
        public double MeanAcquisitionMs { get; set; }
        public double P50AcquisitionMs { get; set; }
        public double P95AcquisitionMs { get; set; }
        public double P99AcquisitionMs { get; set; }
        public double MeanReleaseMs { get; set; }
        public long AllocatedMemoryDeltaBytes { get; set; }
        public int Gen0Collections { get; set; }
        public int Gen1Collections { get; set; }
        public int Gen2Collections { get; set; }
    }
}
