using Avalon.Crashes.Persistence;
using Avalon.Crashes.Validation;
using Avalon.Crashes.Worker.Diagnostics;
using Avalon.Crashes.Worker.Discovery;
using Avalon.Crashes.Worker.Maintenance;
using Avalon.Crashes.Worker.Processing;
using Avalon.Crashes.Worker.Storage;
using Avalon.Crashes.Worker.Symbols;
using Avalon.Database.Crashes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Avalon.Crashes.Worker;

public static class CrashWorkerRegistration
{
    public static void AddCrashDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(_ => configuration.GetSection("Database:Crashes").Get<CrashDatabaseOptions>() ?? new());
        services.AddSingleton<IDbContextFactory<CrashesDbContext>, CrashesDbContextFactory>();
        services.AddSingleton<CrashMaintenanceStore>();
        services.AddSingleton<ICrashMaintenanceStore>(s => s.GetRequiredService<CrashMaintenanceStore>());
        services.AddSingleton<CrashRetentionService>();
        services.AddSingleton<CrashOperatorCommands>();
    }

    public static WebApplication BuildRun(bool paused, Action<WebApplicationBuilder>? configure = null)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });
        CrashTelemetryPrivacy.BeforeDefaults(builder);
        builder.AddServiceDefaults();
        CrashTelemetryPrivacy.AfterDefaults(builder);
        builder.WebHost.ConfigureKestrel(o => o.ListenAnyIP(8080));
        builder.Services.AddCrashDatabase(builder.Configuration);
        builder.Services.AddSingleton(s => new CrashWorkerState(s.GetRequiredService<TimeProvider>()) { Paused = paused });
        builder.Services.AddSingleton<CrashMetrics>();
        builder.Services.AddSingleton<ICrashQueueObserver>(s => s.GetRequiredService<CrashMetrics>());
        builder.Services.AddSingleton<CrashQueue>();
        builder.Services.AddSingleton<ICrashQueue, CrashGuardedQueue>();
        builder.Services.AddSingleton<ICrashDiscoveryStore, CrashDiscoveryStore>();
        builder.Services.AddSingleton<CrashSymbolLock>();
        builder.Services.AddSingleton<ICrashSymbolLock, CrashGuardedSymbolLock>();
        builder.Services.AddSingleton<CrashMigrator>();
        builder.Services.AddSingleton(s => ReadStorage(builder.Configuration, s.GetRequiredService<CrashSymbolOptions>()));
        builder.Services.AddSingleton(_ => builder.Configuration.GetSection("Crashes:Processing").Get<CrashToolOptions>() ?? new());
        builder.Services.AddSingleton(_ => builder.Configuration.GetSection("Crashes:Symbols").Get<CrashSymbolOptions>() ?? new());
        builder.Services.AddSingleton<CrashWorkspace>(s =>
        {
            CrashToolOptions tools = s.GetRequiredService<CrashToolOptions>();
            CrashSymbolOptions symbols = s.GetRequiredService<CrashSymbolOptions>();
            symbols.Validate();
            string scratch = Path.TrimEndingDirectorySeparator(Path.GetFullPath(tools.ScratchRoot));
            string cache = Path.TrimEndingDirectorySeparator(Path.GetFullPath(symbols.CacheRoot));
            StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (scratch.Equals(cache, comparison) || scratch.StartsWith(cache + Path.DirectorySeparatorChar, comparison) || cache.StartsWith(scratch + Path.DirectorySeparatorChar, comparison))
                throw new InvalidOperationException("Scratch and cache must be disjoint.");
            return new CrashWorkspace(tools);
        });
        builder.Services.AddSingleton<CrashToolRunner>();
        builder.Services.AddSingleton<ICrashToolRunner, CrashMeasuredToolRunner>();
        builder.Services.AddSingleton<S3CrashObjectStore>();
        builder.Services.AddSingleton<ICrashObjectStore>(s => new CrashHealthyObjectStore(s.GetRequiredService<S3CrashObjectStore>(), s.GetRequiredService<CrashWorkerState>(), s.GetRequiredService<CrashProcessingBackoff>()));
        builder.Services.AddSingleton<S3CrashSymbolStore>();
        builder.Services.AddSingleton<ICrashSymbolStore>(s => new CrashHealthySymbolStore(s.GetRequiredService<S3CrashSymbolStore>(), s.GetRequiredService<CrashWorkerState>(), s.GetRequiredService<CrashProcessingBackoff>()));
        builder.Services.AddSingleton<SymbolCache>();
        builder.Services.AddSingleton<PublicSymbolClient>();
        builder.Services.AddSingleton<CrashSymbolResolver>();
        builder.Services.AddSingleton<CrashArchiveReader>();
        builder.Services.AddSingleton<ICrashAnalysisPipeline, CrashAnalysisPipeline>();
        builder.Services.AddSingleton<CrashProcessor>();
        builder.Services.AddSingleton<CrashProcessingBackoff>();
        builder.Services.AddSingleton<CrashRetryPolicy>();
        builder.Services.AddSingleton<CrashDiscovery>();
        builder.Services.AddSingleton<ICrashWorkerRuntime, CrashWorkerRuntime>();
        builder.Services.AddHostedService<CrashWorkerService>();
        builder.Services.AddSingleton<CrashNotices>();
        builder.Services.AddHostedService<CrashNoticeService>();
        builder.Services.AddHealthChecks().AddCheck<CrashHealthCheck>("crash_ready").AddCheck<CrashLoopHealthCheck>("crash_loop", tags: ["live"]);
        configure?.Invoke(builder);
        WebApplication app = builder.Build();
        app.MapDefaultEndpoints();
        // Upload-complete notices from the API (#1252), on the internal port only the chart's noticePeers reach.
        app.MapPost("/internal/crash/notice", (CrashNoticeRequest request, CrashNotices notices) =>
            notices.Offer(request.Key) ? Results.Accepted() : Results.BadRequest())
            .WithMetadata(new RequestSizeLimitAttribute(512));
        return app;
    }

    private static CrashStorageOptions ReadStorage(IConfiguration configuration, CrashSymbolOptions symbols)
    {
        CrashStorageOptions options = configuration.GetSection("Crashes:Storage").Get<CrashStorageOptions>() ?? new();
        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out Uri? endpoint) || endpoint.Scheme != "https"
            || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0
            || string.IsNullOrWhiteSpace(options.AccessKeyId) || string.IsNullOrWhiteSpace(options.SecretAccessKey)
            || string.IsNullOrWhiteSpace(options.Region) || string.IsNullOrWhiteSpace(options.Bucket)
            || options.Bucket == symbols.OriginalBucket || options.Bucket == symbols.ConvertedBucket)
        {
            throw new InvalidOperationException("Crash storage configuration invalid.");
        }
        return options;
    }

    private sealed class CrashLoopHealthCheck(CrashWorkerState state) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(state.Alive ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Worker loop stalled."));
    }
}

public sealed class CrashMeasuredToolRunner(CrashToolRunner runner, CrashMetrics metrics) : ICrashToolRunner
{
    public async Task<ToolRunResult> RunAsync(ToolInvocation invocation, CancellationToken ct)
    {
        ToolRunResult result = await runner.RunAsync(invocation, ct);
        if (result.Failure != ToolFailureKind.None) metrics.ToolFailure(invocation.Kind, result.Failure);
        return result;
    }
}
