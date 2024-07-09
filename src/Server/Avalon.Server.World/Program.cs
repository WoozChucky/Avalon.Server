using System.Numerics;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using Avalon.Common.Cryptography;
using Avalon.Common.Telemetry;
using Avalon.Database.Character;
using Avalon.Game;
using Avalon.Hosting;
using Avalon.Infrastructure;
using Avalon.Infrastructure.Configuration;
using Avalon.Infrastructure.World;
using Avalon.Metrics;
using Avalon.Network;
using Avalon.Network.Packets.Abstractions.Attributes;
using Avalon.Network.Packets.Internal;
using Avalon.Network.Packets.Internal.Deserialization;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Tcp;
using Avalon.Server.World.Configuration;
using Avalon.Server.World.Extensions;
using Avalon.Server.World.Logging;
using Avalon.World;
using Avalon.World.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace Avalon.Server.World
{
    internal class Program
    {
        private static CancellationTokenSource CancellationTokenSource { get; set; } = null!;
        private static IServiceProvider ServiceProvider { get; set; } = null!;
        private static IConfigurationRoot Configuration { get; set; } = null!;
        private static IAvalonInfrastructure Infrastructure { get; set; } = null!;
        private static ILogger<Program> Logger { get; set; } = null!;
        private static IMetricsManager MetricsManager { get; set; } = null!;
        private static AppConfiguration AppConfiguration { get; set; } = null!;
        
        private static async Task Main(string[] args)
        {
            var hostBuilder = await AvalonHostBuilder.CreateHostAsync(args, ComponentType.World);
            hostBuilder.Services.AddWorldServices();
            hostBuilder.Services.AddHostedService<WorldServer>();
            hostBuilder.Services.AddSingleton<IWorldServer>(provider =>
                provider.GetServices<IHostedService>().OfType<WorldServer>().Single());
        
            var host = hostBuilder.Build();
        
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var characterDb = scope.ServiceProvider.GetRequiredService<CharacterDbContext>();
                var worldDb = scope.ServiceProvider.GetRequiredService<WorldDbContext>();
                var logger = host.Services.GetRequiredService<ILogger<Program>>();
                logger.LogInformation("Migrating database if necessary...");
                await characterDb.Database.MigrateAsync();
                await worldDb.Database.MigrateAsync();
            
                var cache = scope.ServiceProvider.GetRequiredService<IReplicatedCache>();
                await cache.ConnectAsync();
            }

            await AvalonHostBuilder.RunAsync<Program>(host);
            /*
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledExceptionOccurred;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Console.CancelKeyPress += ConsoleOnCancelKeyPress;
            //AssemblyLoadContext.Default.Unloading += SigTermEventHandler;

            ConfigureConfiguration(args);
            ConfigureDependencyInjection();
            
            try
            {
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.RealTime;
                Process.GetCurrentProcess().PriorityBoostEnabled = true;
            }
            catch (Exception)
            {
                Logger.LogWarning("Failed to set process priority, defaulting to Normal priority");
            }
            
            MetricsManager.Start(new Dictionary<string, string>()
            {
                {"Host", Environment.MachineName },
                {"OS", Environment.OSVersion.VersionString },
                {"SystemPageSize", Environment.SystemPageSize.ToString() },
                {"ProcessorCount", Environment.ProcessorCount.ToString() },
                {"UserDomainName", Environment.UserDomainName },
                {"UserName", Environment.UserName },
                {"Version", Environment.Version.ToString() },
                {"WorkingSet", Environment.WorkingSet.ToString() },
                {"Application", Assembly.GetExecutingAssembly().GetName().Name! },
            });

            Infrastructure.Start();
            
            Infrastructure.Update(CancellationTokenSource);
            
            Logger.LogInformation("Stopping application...");
            
            Infrastructure.Stop();
            Infrastructure.Dispose();
            
            Logger.LogInformation("Terminated successfully");
            */
        }

        private static void ConsoleOnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            Logger.LogWarning("[Ctrl+C] was caught, stopping application...");
            CancellationTokenSource.Cancel();
        }
        
        private static void OnUnhandledExceptionOccurred(object sender, UnhandledExceptionEventArgs e)
        {
            Logger.LogError(e.ExceptionObject as Exception, "Unhandled exception");
            CancellationTokenSource.Cancel();
        }
        
        private static void OnProcessExit(object? sender, EventArgs e)
        {
            Logger.LogInformation("Exited successfully");
        }

        private static void ConfigureConfiguration(string[] args)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", false, false)
                .AddEnvironmentVariables();
            
            if (args.Length > 0)
            {
                builder.AddCommandLine(args);
            }
            
            Configuration = builder.Build();

            AppConfiguration = new AppConfiguration();
            
            Configuration.Bind(AppConfiguration);
        }
        
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
            services.AddSingleton<IAvalonSessionManager, AvalonSessionManager>();
            services.AddSingleton<IPacketDeserializer, NetworkPacketDeserializer>();
            services.AddSingleton<IPacketSerializer, NetworkPacketSerializer>();
            services.AddSingleton<IPacketRegistry, PacketRegistry>();
            services.AddSingleton<IAvalonNetworkDaemon, AvalonWorldNetworkDaemon>();
            services.AddSingleton<IAvalonGame, AvalonGame>();
            services.AddSingleton<ICryptoManager, CryptoManager>();
            services.AddSingleton<IAvalonInfrastructure, AvalonWorldInfrastructure>();
            services.AddSingleton<CancellationTokenSource>(s => new CancellationTokenSource());
            services.AddScoped<IReplicatedCache, ReplicatedCache>(provider =>
            {
                var options = provider.GetRequiredService<IOptionsSnapshot<CacheConfiguration>>();
                return new ReplicatedCache(provider.GetRequiredService<ILoggerFactory>(), options);
            });
            
            ServiceProvider = services.BuildServiceProvider();
            
            Logger = ServiceProvider.GetService<ILogger<Program>>() ?? throw new InvalidOperationException();
            Infrastructure = ServiceProvider.GetService<IAvalonInfrastructure>() ?? throw new InvalidOperationException();
            CancellationTokenSource = ServiceProvider.GetService<CancellationTokenSource>() ?? throw new InvalidOperationException();
            MetricsManager = ServiceProvider.GetService<IMetricsManager>() ?? throw new InvalidOperationException();
        }
    }
}
