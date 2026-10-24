using System.Diagnostics.Metrics;
using Avalon.Crashes.Persistence;
using Avalon.Crashes.Validation;

namespace Avalon.Crashes.Worker.Diagnostics;

public sealed class CrashMetrics : IDisposable, ICrashQueueObserver
{
    public const string MeterName = "avalon-crashes";
    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _reports;
    private readonly Counter<long> _groups;
    private readonly Counter<long> _retries;
    private readonly Counter<long> _recoveries;
    private readonly Counter<long> _toolFailures;
    private readonly Counter<long> _cache;
    private readonly Counter<long> _publicFailures;
    private readonly Counter<long> _uploadFailures;
    private readonly Counter<long> _quality;
    private readonly Histogram<double> _duration;
    private readonly HashSet<string> _versions = new(StringComparer.Ordinal);
    private CrashOperationalSnapshot _snapshot = new(0, 0, 0, 0, 0, 0, 0, 0);
    public CrashMetrics()
    {
        _reports = _meter.CreateCounter<long>("avalon.crashes.reports");
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
        _reports.Add(1, new KeyValuePair<string, object?>("version", VersionLabel(version)),
            new KeyValuePair<string, object?>("status", status.ToString().ToLowerInvariant()));
    }
    public string VersionLabel(string? version)
    {
        if (version is null || !CrashObjectKey.IsVersion(version))
            return "other";
        lock (_versions)
        {
            if (_versions.Contains(version)) return version;
            if (_versions.Count >= 100) return "other";
            _versions.Add(version);
            return version;
        }
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
