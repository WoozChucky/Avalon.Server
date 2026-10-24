using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalon.Crashes.Worker.Diagnostics;
using Avalon.Database.Crashes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace Avalon.Crashes.IntegrationTests.Maintenance;

public sealed class CrashTelemetryPrivacyShould : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    public Task InitializeAsync() => _postgres.InitializeAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync();

    [IntegrationFact]
    public async Task Should_keep_actual_http_urls_and_sensitive_ef_parameters_out_of_all_exporters()
    {
        const string Sentinel = "sentinel-private-key-path-signed-query-sql";
        var exports = new ConcurrentQueue<string>();
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        CrashTelemetryPrivacy.BeforeDefaults(builder);
        builder.AddServiceDefaults();
        CrashTelemetryPrivacy.AfterDefaults(builder);
        builder.Services.AddHttpClient("private");
        builder.Services.AddOpenTelemetry()
            .WithTracing(t => t.AddProcessor(new SimpleActivityExportProcessor(new Capture<Activity>(exports,
                a => a.DisplayName + string.Join(',', a.TagObjects) + string.Join(',', a.Events.Select(e => e.Name + string.Join(',', e.Tags)))))))
            .WithMetrics(m => m.AddReader(new BaseExportingMetricReader(new Capture<Metric>(exports, FormatMetric))));
        builder.Logging.AddOpenTelemetry(o => o.AddProcessor(new SimpleLogRecordExportProcessor(new Capture<LogRecord>(exports,
            r => r.Body + r.FormattedMessage + r.Exception + string.Join(',', r.Attributes ?? [])))));
        using IHost host = builder.Build();
        await host.StartAsync();
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task respond = RespondAsync(server, timeout.Token);
        using HttpClient client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("private");
        using HttpResponseMessage response = await client.GetAsync($"http://127.0.0.1:{((IPEndPoint)server.LocalEndpoint).Port}/{Sentinel}?signature={Sentinel}", timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await respond;

        await using CrashesDbContext original = _postgres.Factory.CreateDbContext();
        var connection = new NpgsqlConnectionStringBuilder(original.Database.GetConnectionString()) { Password = Sentinel, IncludeErrorDetail = true };
        DbContextOptions<CrashesDbContext> options = new DbContextOptionsBuilder<CrashesDbContext>()
            .UseNpgsql(connection.ConnectionString).EnableSensitiveDataLogging().EnableDetailedErrors()
            .UseLoggerFactory(host.Services.GetRequiredService<ILoggerFactory>()).Options;
        await using var db = new CrashesDbContext(options);
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlAsync($"SELECT CAST({Sentinel} AS integer)", timeout.Token));
        using var source = new ActivitySource(CrashMetrics.MeterName);
        using (source.StartActivity("process")) { }
        host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Avalon.Crashes.Worker.Events")
            .LogWarning(new IOException(Sentinel), "{secret}", Sentinel);
        using var metrics = new CrashMetrics();
        metrics.Terminal("1.2.3", CrashReportStatus.Processed);
        host.Services.GetRequiredService<MeterProvider>().ForceFlush();
        host.Services.GetRequiredService<TracerProvider>().ForceFlush();
        Assert.Contains(exports, x => x.StartsWith("process", StringComparison.Ordinal));
        Assert.Contains(exports, x => x.StartsWith("avalon.crashes.reports", StringComparison.Ordinal));
        Assert.Contains(exports, x => x.Contains("Crash worker event.", StringComparison.Ordinal));
        Assert.DoesNotContain(Sentinel, string.Join('\n', exports));
        await host.StopAsync();
    }

    private static string FormatMetric(Metric metric)
    {
        string value = metric.Name;
        foreach (ref readonly MetricPoint point in metric.GetMetricPoints())
            foreach (KeyValuePair<string, object?> tag in point.Tags) value += tag.ToString();
        return value;
    }

    private static async Task RespondAsync(TcpListener server, CancellationToken ct)
    {
        using TcpClient connection = await server.AcceptTcpClientAsync(ct);
        using NetworkStream stream = connection.GetStream();
        byte[] buffer = new byte[4096];
        _ = await stream.ReadAsync(buffer, ct);
        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), ct);
    }

    private sealed class Capture<T>(ConcurrentQueue<string> output, Func<T, string> format) : BaseExporter<T> where T : class
    {
        public override ExportResult Export(in Batch<T> batch)
        {
            foreach (T item in batch) output.Enqueue(format(item));
            return ExportResult.Success;
        }
    }
}
