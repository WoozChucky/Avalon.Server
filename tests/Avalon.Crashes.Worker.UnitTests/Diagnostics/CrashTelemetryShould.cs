using System.Diagnostics;
using System.Diagnostics.Metrics;
using Avalon.Crashes.Worker.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace Avalon.Crashes.Worker.UnitTests.Diagnostics;

public sealed class CrashTelemetryShould
{
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
}
