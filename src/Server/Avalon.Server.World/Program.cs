using Avalon.Database.Character;
using Avalon.Database.World;
using Avalon.Hosting;
using Avalon.Infrastructure;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Server.World.Extensions;
using Avalon.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Avalon.Server.World;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Thread.CurrentThread.Name ??= "main";

        HostApplicationBuilder hostBuilder = await AvalonHostBuilder.CreateHostAsync(args, ComponentType.World);
        hostBuilder.Services
            .AddWorldServices()
            .AddHostedService<WorldServer>()
            .AddSingleton<IWorldServer>(provider =>
                provider.GetServices<IHostedService>().OfType<WorldServer>().Single());

        IHost host = hostBuilder.Build();

        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
        {
            CharacterDbContext characterDb = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();
            WorldDbContext worldDb = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
            host.Services.GetRequiredService<ILogger<Program>>().LogInformation("Migrating database if necessary...");
            await characterDb.Database.MigrateAsync();
            await worldDb.Database.MigrateAsync();

            IReplicatedCache cache = scope.ServiceProvider.GetRequiredService<IReplicatedCache>();
            await cache.ConnectAsync();
        }

        await AvalonHostBuilder.RunAsync<Program>(host);
    }

    /*
    private static void ConfigureDependencyInjection()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder
                .AddSerilog(new LoggerConfiguration()
                    .MinimumLevel.Verbose()
                    .Enrich.FromLogContext()
                    .Enrich.WithThreadId()
                    .Enrich.With<LayerEnricher>() // ({SourceContext})
                    .WriteTo.Console(LogEventLevel.Debug, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] [{ThreadId}] [{Layer}] -> {Message}{NewLine}{Exception}", theme: AnsiConsoleTheme.Sixteen, applyThemeToRedirectedOutput: true)
                    .CreateLogger()
                )
                .SetMinimumLevel(LogLevel.Debug);
            if (AppConfiguration.Metrics!.Export)
            {
                builder.AddOpenTelemetry(options =>
                {
                    options.SetResourceBuilder(
                            ResourceBuilder
                                .CreateDefault()
                                .AddService(DiagnosticsConfig.Server.ServiceName)
                        )
                        .AddOtlpExporter(options =>
                        {
                            options.Protocol = OtlpExportProtocol.Grpc;
                            options.Endpoint = new Uri("http://192.168.1.227:4317");
                        });
                });
            }
        });

        if (AppConfiguration.Metrics!.Export)
        {
            services.AddOpenTelemetry()
                .ConfigureResource(builder =>
                {
                    builder
                        .AddService(DiagnosticsConfig.Server.ServiceName)
                        .AddAttributes(new Dictionary<string, object>()
                        {
                            {"Host", Environment.MachineName},
                            {"OS", Environment.OSVersion.VersionString},
                            {"SystemPageSize", Environment.SystemPageSize.ToString()},
                            {"ProcessorCount", Environment.ProcessorCount.ToString()},
                            {"UserDomainName", Environment.UserDomainName},
                            {"UserName", Environment.UserName},
                            {"Version", Environment.Version.ToString()},
                            {"WorkingSet", Environment.WorkingSet.ToString()},
                            {"Application", Assembly.GetExecutingAssembly().GetName().Name!},
                        })
                        .AddContainerDetector();
                })
                .WithMetrics(builder =>
                {
                    builder
                        .AddMeter(DiagnosticsConfig.Server.Meter.Name)
                        .AddRuntimeInstrumentation()
                        .AddProcessInstrumentation()
                        .AddOtlpExporter(options =>
                        {
                            options.Protocol = OtlpExportProtocol.Grpc;
                            options.Endpoint = new Uri("http://192.168.1.227:4317");
                        });
                })
                .WithTracing(builder =>
                {
                    builder
                        .AddOtlpExporter(options =>
                        {
                            options.Protocol = OtlpExportProtocol.Grpc;
                            options.Endpoint = new Uri("http://192.168.1.227:4317");
                        });
                });
        }

        services.AddSingleton(AppConfiguration);
        services.AddSingleton(AppConfiguration.Infrastructure!);
        services.AddSingleton(AppConfiguration.NetworkDaemon!);
        services.AddSingleton(AppConfiguration.NetworkDaemon!.Tcp!);
        services.AddSingleton(AppConfiguration.Metrics);
        services.AddSingleton(AppConfiguration.Database!);
        services.AddSingleton(AppConfiguration.Cache!);
        services.AddSingleton(AppConfiguration.Game!);

        if (AppConfiguration.Metrics.Enabled)
        {
            services.AddSingleton<IMetricsManager, MetricsManager>();
        }
        else
        {
            services.AddSingleton<IMetricsManager, FakeMetricsManager>();
        }

        services.AddSingleton<IAvalonTcpServer, AvalonTcpServer>();
        services.AddSingleton<IPacketDeserializer, NetworkPacketDeserializer>();
        services.AddSingleton<IPacketSerializer, NetworkPacketSerializer>();
        services.AddSingleton<IPacketRegistry, PacketRegistry>();
        services.AddSingleton<ICryptoManager, CryptoManager>();
        services.AddSingleton<CancellationTokenSource>(s => new CancellationTokenSource());
        services.AddScoped<IReplicatedCache, ReplicatedCache>(provider =>
        {
            var options = provider.GetRequiredService<IOptionsSnapshot<CacheConfiguration>>();
            return new ReplicatedCache(provider.GetRequiredService<ILoggerFactory>(), options);
        });
    }
    */
}
