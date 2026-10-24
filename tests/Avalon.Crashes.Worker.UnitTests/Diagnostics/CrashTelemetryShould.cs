using System.Diagnostics;
using System.Diagnostics.Metrics;
using Avalon.Crashes.Worker.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace Avalon.Crashes.Worker.UnitTests.Diagnostics;

[Collection("Crash telemetry")]
public sealed class CrashTelemetryShould
{
    [Fact]
    public void Should_rotate_inactive_versions_without_retaining_unbounded_export_series_or_recounting()
    {
        var clock = new FakeTimeProvider();
        using var metrics = new CrashMetrics(clock);
        var batches = new List<ReportBatch>();
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        CrashTelemetryPrivacy.BeforeDefaults(builder);
        builder.AddServiceDefaults();
        CrashTelemetryPrivacy.AfterDefaults(builder);
        var exporter = new Capture<Metric>(metric =>
        {
            if (metric.Name != "avalon.crashes.reports") return string.Empty;
            var points = new Dictionary<(string Version, string Status), long>();
            foreach (ref readonly MetricPoint point in metric.GetMetricPoints())
            {
                string? version = null;
                string? status = null;
                foreach (KeyValuePair<string, object?> tag in point.Tags)
                {
                    if (tag.Key == "version") version = (string)tag.Value!;
                    else if (tag.Key == "status") status = (string)tag.Value!;
                    else Assert.Fail("Unexpected metric label, including SDK overflow.");
                }
                points.Add((version!, status!), point.GetSumLong());
            }
            batches.Add(new(metric.Temporality, points));
            return string.Empty;
        });
        builder.Services.AddOpenTelemetry().WithMetrics(m => m.AddReader(services => new BaseExportingMetricReader(exporter)
        {
            TemporalityPreference = services.GetRequiredService<IOptions<MetricReaderOptions>>().Value.TemporalityPreference
        }));
        using IHost host = builder.Build();
        MeterProvider provider = host.Services.GetRequiredService<MeterProvider>();
        ReportBatch Collect()
        {
            batches.Clear();
            Assert.True(provider.ForceFlush());
            return batches.Count == 0 ? new(AggregationTemporality.Delta, []) : Assert.Single(batches);
        }

        CrashReportStatus[] statuses = [CrashReportStatus.Processed, CrashReportStatus.Rejected, CrashReportStatus.Failed];
        for (int i = 0; i < 100; i++)
            foreach (CrashReportStatus status in statuses) metrics.Terminal($"1.2.{i}", status);
        metrics.Terminal("not/a/version", CrashReportStatus.Failed);
        ReportBatch initial = Collect();
        Assert.Equal(301, initial.Points.Count);
        Assert.Equal(301, initial.Points.Values.Sum());
        Assert.Equal(0, Collect().Points.Values.Sum());

        clock.Advance(TimeSpan.FromHours(23));
        metrics.Terminal("1.2.0", CrashReportStatus.Processed);
        Assert.Equal(1, Collect().Points.Values.Sum());
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(0, Collect().Points.Values.Sum()); // collection retires the other99, not the recently used release
        metrics.Terminal("2.0.0", CrashReportStatus.Processed);
        ReportBatch fresh = Collect();
        Assert.Equal(1, fresh.Points[("2.0.0", "processed")]);
        Assert.DoesNotContain(fresh.Points.Keys, p => p.Version == "1.2.1");
        metrics.Terminal("1.2.0", CrashReportStatus.Processed);
        metrics.Terminal("1.2.1", CrashReportStatus.Processed); // same label reactivated after retirement
        Assert.Equal(2, Collect().Points.Values.Sum());
        Assert.Equal(0, Collect().Points.Values.Sum());

        // More cohorts than the SDK's default lifetime cardinality: every collection still admits new labels.
        for (int generation = 3; generation < 15; generation++)
        {
            clock.Advance(TimeSpan.FromHours(25));
            Assert.Equal(0, Collect().Points.Values.Sum());
            for (int i = 0; i < 101; i++)
                foreach (CrashReportStatus status in statuses) metrics.Terminal($"{generation}.0.{i}", status);
            ReportBatch batch = Collect();
            Assert.Equal(AggregationTemporality.Delta, batch.Temporality);
            Assert.Equal(303, batch.Points.Count);
            Assert.Equal(303, batch.Points.Values.Sum());
            Assert.Equal(101, batch.Points.Keys.Select(p => p.Version).Distinct().Count());
            Assert.Equal(1, batch.Points[($"{generation}.0.99", "processed")]);
            Assert.Equal(0, Collect().Points.Values.Sum());
        }
    }

    [Fact]
    public void Should_bound_versions_and_not_export_unvalidated_labels()
    {
        using var metrics = new CrashMetrics();
        var labels = new HashSet<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meter) => { if (instrument.Meter.Name == CrashMetrics.MeterName) meter.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((_, _, tags, _) =>
        {
            foreach (KeyValuePair<string, object?> tag in tags)
                if (tag.Key == "version") labels.Add((string)tag.Value!);
        });
        listener.Start();
        for (int i = 0; i < 200; i++) metrics.Terminal($"1.2.{i}", CrashReportStatus.Processed);
        metrics.Terminal("secret/key?token=sentinel", CrashReportStatus.Failed);
        listener.RecordObservableInstruments();
        Assert.Equal(101, labels.Count);
        Assert.Contains("other", labels);
        Assert.DoesNotContain("secret/key?token=sentinel", labels);
    }

    [Fact]
    public void Should_keep_http_and_ef_secrets_out_of_exported_telemetry()
    {
        const string Sentinel = "sentinel-secret-key-path-query-sql-value";
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        CrashTelemetryPrivacy.BeforeDefaults(builder);
        builder.AddServiceDefaults();
        CrashTelemetryPrivacy.AfterDefaults(builder);
        var traces = new Capture<Activity>(a => a.DisplayName + string.Join(',', a.TagObjects) + string.Join(',', a.Events.Select(e => e.Name + string.Join(',', e.Tags))));
        var logs = new Capture<LogRecord>(r => r.Body + r.FormattedMessage + r.Exception + string.Join(',', r.Attributes ?? []));
        var metrics = new Capture<Metric>(m =>
        {
            string data = m.Name;
            foreach (ref readonly MetricPoint point in m.GetMetricPoints())
                foreach (KeyValuePair<string, object?> tag in point.Tags) data += tag.ToString();
            return data;
        });
        builder.Services.AddOpenTelemetry().WithTracing(t => t.AddSource("System.Net.Http", "Microsoft.EntityFrameworkCore", "avalon-crashes").AddProcessor(new SimpleActivityExportProcessor(traces)))
            .WithMetrics(m => m.AddMeter("System.Net.Http", CrashMetrics.MeterName).AddReader(new BaseExportingMetricReader(metrics)));
        builder.Logging.AddOpenTelemetry(o => o.AddProcessor(new SimpleLogRecordExportProcessor(logs)));
        using IHost host = builder.Build();
        _ = host.Services.GetRequiredService<TracerProvider>();
        _ = host.Services.GetRequiredService<MeterProvider>();
        using var http = new ActivitySource("System.Net.Http");
        using var ef = new ActivitySource("Microsoft.EntityFrameworkCore");
        foreach (ActivitySource source in new[] { http, ef })
        {
            using Activity? span = source.StartActivity("process");
            span?.SetTag("url.full", "https://localhost/" + Sentinel);
            span?.SetTag("db.statement", "SELECT '" + Sentinel + "'");
            span?.AddEvent(new ActivityEvent(Sentinel));
        }
        using var safe = new ActivitySource("avalon-crashes");
        using (safe.StartActivity("process")) { }
        ILoggerFactory factory = host.Services.GetRequiredService<ILoggerFactory>();
        factory.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command").LogError(new Exception(Sentinel), "{Sql}", Sentinel);
        using (factory.CreateLogger("Avalon.Crashes.Worker.Events").BeginScope(Sentinel))
            factory.CreateLogger("Avalon.Crashes.Worker.Events").LogError(new Exception(Sentinel), "{Secret}", Sentinel);
        using var httpMeter = new Meter("System.Net.Http");
        httpMeter.CreateCounter<long>("http.client.requests").Add(1, new KeyValuePair<string, object?>("url.full", Sentinel));
        using var crashMetrics = new CrashMetrics();
        crashMetrics.Terminal("1.2.3", CrashReportStatus.Processed);
        host.Services.GetRequiredService<MeterProvider>().ForceFlush();
        host.Services.GetRequiredService<TracerProvider>().ForceFlush();
        Assert.NotEmpty(metrics.Items);
        Assert.Single(traces.Items);
        Assert.NotEmpty(logs.Items);
        Assert.DoesNotContain(Sentinel, string.Join('\n', traces.Items.Concat(logs.Items).Concat(metrics.Items)));
    }

    private sealed class Capture<T>(Func<T, string> format) : BaseExporter<T> where T : class
    {
        public List<string> Items { get; } = [];
        public override ExportResult Export(in Batch<T> batch)
        {
            foreach (T item in batch) Items.Add(format(item));
            return ExportResult.Success;
        }
    }
    private sealed record ReportBatch(AggregationTemporality Temporality, Dictionary<(string Version, string Status), long> Points);
}

[CollectionDefinition("Crash telemetry", DisableParallelization = true)]
public sealed class CrashTelemetryCollection;
