using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Instrumentation.EntityFrameworkCore;
using OpenTelemetry.Instrumentation.Http;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Avalon.Crashes.Worker.Diagnostics;

public static class CrashTelemetryPrivacy
{
    // Register the scrubber BEFORE shared defaults attach exporters. No unsanitized console provider remains.
    public static void BeforeDefaults(IHostApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddOpenTelemetry(o => o.AddProcessor(new PrivateLogs()));
        builder.Services.AddOpenTelemetry().WithTracing(t => t.AddProcessor(new PrivateSpans()));
    }

    public static void AfterDefaults(IHostApplicationBuilder builder)
    {
        builder.Services.PostConfigure<HttpClientTraceInstrumentationOptions>(o => o.FilterHttpRequestMessage = _ => false);
        builder.Services.PostConfigure<EntityFrameworkInstrumentationOptions>(o => o.Filter = (_, _) => false);
        builder.Services.PostConfigure<AspNetCoreTraceInstrumentationOptions>(o => { o.Filter = _ => false; o.RecordException = false; });
        builder.Services.PostConfigure<OpenTelemetryLoggerOptions>(o =>
        {
            o.IncludeScopes = false;
            o.IncludeFormattedMessage = false;
            o.ParseStateValues = true;
        });
        builder.Services.PostConfigure<LoggerFilterOptions>(o =>
        {
            o.Rules.Clear();
            o.Rules.Add(new LoggerFilterRule(null, null, LogLevel.Information,
                (_, category, _) => category == "Avalon.Crashes.Worker.Events"));
        });
        builder.Services.AddOpenTelemetry()
            .WithTracing(t => t.SetSampler(new PrivateTraces()).AddSource(CrashMetrics.MeterName))
            .WithMetrics(m => m.AddMeter(CrashMetrics.MeterName).AddView(instrument =>
                instrument.Meter.Name == CrashMetrics.MeterName ? null : MetricStreamConfiguration.Drop));
    }

    private sealed class PrivateTraces : Sampler
    {
        public override SamplingResult ShouldSample(in SamplingParameters parameters) =>
            new(parameters.Name == "process" && parameters.Tags is null && parameters.Kind == ActivityKind.Internal
                ? SamplingDecision.RecordAndSample : SamplingDecision.Drop);
    }

    private sealed class PrivateSpans : BaseProcessor<Activity>
    {
        public override void OnStart(Activity data)
        {
            // Samplers receive the operation name, not the source. Enforce both before any exporter.
            if (data.Source.Name != CrashMetrics.MeterName || data.OperationName != "process")
            {
                data.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
                data.IsAllDataRequested = false;
            }
        }
    }

    private sealed class PrivateLogs : BaseProcessor<LogRecord>
    {
        public override void OnEnd(LogRecord data)
        {
            // Allow only typed finite diagnostics, never a formatter, exception, arbitrary state or scope.
            KeyValuePair<string, object?>[] safe = (data.Attributes ?? []).Where(x =>
                x.Key == "report_id" && x.Value is Guid
                || x.Key == "reason" && x.Value is CrashFailureCode code && Enum.IsDefined(code)).ToArray();
            data.Exception = null;
            data.Attributes = safe;
            data.Body = "Crash worker event.";
            data.FormattedMessage = "Crash worker event.";
            data.EventId = default;
        }
    }
}
