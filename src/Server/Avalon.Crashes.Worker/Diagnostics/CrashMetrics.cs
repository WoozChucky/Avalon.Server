using System.Diagnostics.Metrics;
using Avalon.Crashes.Persistence;
using Avalon.Crashes.Validation;

namespace Avalon.Crashes.Worker.Diagnostics;

public sealed class CrashMetrics : IDisposable, ICrashQueueObserver
{
    public const string MeterName = "avalon-crashes";
    public const int ReportSeriesLimit = 606; // current and retiring cohorts: 2 * (100 versions + other) * 3 statuses
    public static readonly TimeSpan VersionInactivityWindow = TimeSpan.FromHours(24);
    private static readonly string[] s_terminalStatuses = ["processed", "rejected", "failed"];
    private readonly Meter _meter = new(MeterName);
    private readonly TimeProvider _time;
    private readonly Counter<long> _groups;
    private readonly Counter<long> _retries;
    private readonly Counter<long> _recoveries;
    private readonly Counter<long> _toolFailures;
    private readonly Counter<long> _cache;
    private readonly Counter<long> _publicFailures;
    private readonly Counter<long> _uploadFailures;
    private readonly Counter<long> _quality;
    private readonly Histogram<double> _duration;
    private readonly Dictionary<string, VersionSeries> _versions = new(StringComparer.Ordinal);
    private readonly long[] _otherReports = new long[3];
    private CrashOperationalSnapshot _snapshot = new(0, 0, 0, 0, 0, 0, 0, 0);
    public CrashMetrics(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
        _meter.CreateObservableCounter("avalon.crashes.reports", ObserveReports);
        _groups = _meter.CreateCounter<long>("avalon.crashes.groups_new");
        _retries = _meter.CreateCounter<long>("avalon.crashes.retries");
        _recoveries = _meter.CreateCounter<long>("avalon.crashes.lease_recoveries");
        _toolFailures = _meter.CreateCounter<long>("avalon.crashes.tool_failures");
        _cache = _meter.CreateCounter<long>("avalon.crashes.symbol_cache");
        _publicFailures = _meter.CreateCounter<long>("avalon.crashes.public_symbol_failures");
        _uploadFailures = _meter.CreateCounter<long>("avalon.crashes.cache_upload_failures");
        _quality = _meter.CreateCounter<long>("avalon.crashes.analysis_quality");
        _duration = _meter.CreateHistogram<double>("avalon.crashes.processing.duration", "s");
        _meter.CreateObservableGauge("avalon.crashes.queue_depth", () => Volatile.Read(ref _snapshot).QueueDepth);
        _meter.CreateObservableGauge("avalon.crashes.oldest_age", () => Volatile.Read(ref _snapshot).OldestAge, "s");
        _meter.CreateObservableGauge("avalon.crashes.inventory_sweep_age", () => Volatile.Read(ref _snapshot).SweepAge, "s");
        _meter.CreateObservableGauge("avalon.crashes.cleanup_last_success", () => Volatile.Read(ref _snapshot).CleanupSuccess, "s");
        _meter.CreateObservableGauge("avalon.crashes.expired_rows", () => Volatile.Read(ref _snapshot).ExpiredRows);
        _meter.CreateObservableGauge("avalon.crashes.retention_cutoff", () => Volatile.Read(ref _snapshot).RetentionCutoff, "s");
        _meter.CreateObservableGauge("avalon.crashes.scratch_bytes", () => Volatile.Read(ref _snapshot).ScratchBytes, "By");
        _meter.CreateObservableGauge("avalon.crashes.cache_bytes", () => Volatile.Read(ref _snapshot).CacheBytes, "By");
    }
    public void Terminal(string? version, CrashReportStatus status)
    {
        if (status is not (CrashReportStatus.Processed or CrashReportStatus.Failed or CrashReportStatus.Rejected))
            return;
        lock (_versions)
        {
            long[] counts = _otherReports;
            if (version is not null && version != "other" && CrashObjectKey.IsVersion(version))
            {
                if (!_versions.TryGetValue(version, out VersionSeries? series) && _versions.Count < 100)
                    _versions.Add(version, series = new VersionSeries());
                if (series is not null)
                {
                    series.LastSeen = _time.GetTimestamp();
                    counts = series.Counts;
                }
            }
            counts[status switch { CrashReportStatus.Processed => 0, CrashReportStatus.Rejected => 1, _ => 2 }]++;
        }
    }

    private IEnumerable<Measurement<long>> ObserveReports()
    {
        lock (_versions)
        {
            // Retire only at collection boundaries. The delta reader observes absence and reclaims old SDK points
            // before a newly admitted/reactivated series is observed by the next collection.
            foreach (string version in _versions.Where(x => _time.GetElapsedTime(x.Value.LastSeen) >= VersionInactivityWindow).Select(x => x.Key).ToArray())
                _versions.Remove(version);
            var observations = new List<Measurement<long>>(303);
            foreach ((string version, VersionSeries series) in _versions)
                AddObservations(observations, version, series.Counts);
            AddObservations(observations, "other", _otherReports);
            return observations;
        }
    }

    private static void AddObservations(List<Measurement<long>> observations, string version, long[] counts)
    {
        for (int i = 0; i < counts.Length; i++)
        {
            if (counts[i] > 0)
                observations.Add(new(counts[i], new KeyValuePair<string, object?>("version", version), new KeyValuePair<string, object?>("status", s_terminalStatuses[i])));
        }
    }

    private sealed class VersionSeries
    {
        public long LastSeen;
        public long[] Counts { get; } = new long[3];
    }
    public void GroupCreated() => _groups.Add(1);
    public void Retry() => _retries.Add(1);
    public void LeaseRecovered() => _recoveries.Add(1);
    public void Duration(TimeSpan elapsed) => _duration.Record(elapsed.TotalSeconds);
    public void Cache(bool hit) => _cache.Add(1, new KeyValuePair<string, object?>("result", hit ? "hit" : "miss"));
    public void CacheUploadFailed() => _uploadFailures.Add(1);
    public void PublicFailure() => _publicFailures.Add(1);
    public void ToolFailure(Processing.CrashToolKind tool, Processing.ToolFailureKind reason)
    {
        _toolFailures.Add(1, new KeyValuePair<string, object?>("tool", tool == Processing.CrashToolKind.DumpSymbols ? "conversion" : "stackwalk"),
            new KeyValuePair<string, object?>("reason", Enum.IsDefined(reason) ? reason.ToString() : "other"));
    }
    public void Quality(CrashQualityFlags quality)
    {
        foreach (CrashQualityFlags flag in Enum.GetValues<CrashQualityFlags>())
        {
            if (flag == CrashQualityFlags.None ? quality == flag : quality.HasFlag(flag))
                _quality.Add(1, new KeyValuePair<string, object?>("quality", flag.ToString()));
        }
    }
    public void Snapshot(CrashOperationalSnapshot snapshot) => Interlocked.Exchange(ref _snapshot, snapshot);
    public void Dispose() => _meter.Dispose();
}

public sealed record CrashOperationalSnapshot(long QueueDepth, double OldestAge, double SweepAge,
    long CleanupSuccess, long ExpiredRows, long RetentionCutoff, long ScratchBytes, long CacheBytes);
