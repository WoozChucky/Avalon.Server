using System.Diagnostics;
using System.Reflection;
using Avalon.Common.Telemetry;
using Avalon.Database;
using Avalon.Database.Auth;
using Avalon.Database.Characters;
using Avalon.Database.World;
using Avalon.Game;
using Avalon.Game.Creatures;
using Avalon.Game.Maps;
using Avalon.Game.Pools;
using Avalon.Game.Quests;
using Avalon.Game.Scripts;
using Avalon.Infrastructure;
using Avalon.Metrics;
using Avalon.Network;
using Avalon.Network.Packets.Internal;
using Avalon.Network.Packets.Internal.Deserialization;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Tcp;
using Avalon.Network.Udp;
using Avalon.Server.Configuration;
using Avalon.Server.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.ResourceDetectors.Container;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace Avalon.Server
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
        private static SystemUsageCollector SystemUsageCollector { get; set; } = null!;

        private static async Task Main(string[] args)
        {
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
            catch (Exception e)
            {
                Logger.LogWarning("Failed to set process priority, defaulting to Normal priority");
            }
            
            SystemUsageCollector.Start();
            
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
            
            SystemUsageCollector.Stop();
            SystemUsageCollector.Dispose();
            
            Logger.LogInformation("Terminated successfully");
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
                        .WriteTo.Console(LogEventLevel.Debug, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] [{ThreadId}] [{Layer}] -> {Message}{NewLine}{Exception}", theme: AnsiConsoleTheme.Sixteen)
                        .CreateLogger()
                    )
                    .SetMinimumLevel(LogLevel.Debug);
                if (AppConfiguration.Metrics.Export)
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

            if (AppConfiguration.Metrics.Export)
            {
                services.AddOpenTelemetry()
                    .ConfigureResource(builder =>
                    {
                        builder
                            .AddService(DiagnosticsConfig.Server.ServiceName)
                            .AddAttributes(new Dictionary<string, object>()
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
                            })
                            .AddDetector(new ContainerResourceDetector());
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
            services.AddSingleton(AppConfiguration.Infrastructure);
            services.AddSingleton(AppConfiguration.NetworkDaemon);
            services.AddSingleton(AppConfiguration.NetworkDaemon.Udp);
            services.AddSingleton(AppConfiguration.NetworkDaemon.Tcp);
            services.AddSingleton(AppConfiguration.Metrics);
            services.AddSingleton(AppConfiguration.Database);

            if (AppConfiguration.Metrics.Enabled)
            {
                services.AddSingleton<IMetricsManager, MetricsManager>();
            }
            else
            {
                services.AddSingleton<IMetricsManager, FakeMetricsManager>();
            }
            
            services.AddSingleton<AllocationsListener>();
            services.AddSingleton<SystemUsageCollector>();

            services.AddSingleton<IAuthDatabase, AuthDatabase>();
            services.AddSingleton<ICharactersDatabase, CharactersDatabase>();
            services.AddSingleton<IWorldDatabase, WorldDatabase>();
            services.AddSingleton<IDatabaseManager, DatabaseManager>();
            
            services.AddSingleton<IAvalonTcpServer, AvalonTcpServer>();
            services.AddSingleton<IAvalonUdpServer, ENetUdpServer>();
            services.AddSingleton<IAvalonSessionManager, AvalonSessionManager>();
            services.AddSingleton<IPacketDeserializer, NetworkPacketDeserializer>();
            services.AddSingleton<IPacketSerializer, NetworkPacketSerializer>();
            services.AddSingleton<IPacketRegistry, PacketRegistry>();
            services.AddSingleton<IAvalonNetworkDaemon, AvalonNetworkDaemon>();
            services.AddSingleton<IAvalonMapManager, AvalonMapManager>();
            services.AddSingleton<ICreatureSpawner, CreatureSpawner>();
            services.AddSingleton<IPoolManager, PoolManager>();
            services.AddSingleton<IAIController, AIController>();
            services.AddSingleton<IQuestManager, QuestManager>();
            services.AddSingleton<IAvalonGame, AvalonGame>();
            services.AddSingleton<ICryptoManager, CryptoManager>();
            services.AddSingleton<IAvalonInfrastructure, AvalonInfrastructure>();
            services.AddSingleton<CancellationTokenSource>(s => new CancellationTokenSource());
            
            ServiceProvider = services.BuildServiceProvider();
            
            Logger = ServiceProvider.GetService<ILogger<Program>>() ?? throw new InvalidOperationException();
            Infrastructure = ServiceProvider.GetService<IAvalonInfrastructure>() ?? throw new InvalidOperationException();
            CancellationTokenSource = ServiceProvider.GetService<CancellationTokenSource>() ?? throw new InvalidOperationException();
            MetricsManager = ServiceProvider.GetService<IMetricsManager>() ?? throw new InvalidOperationException();
            SystemUsageCollector = ServiceProvider.GetService<SystemUsageCollector>() ?? throw new InvalidOperationException();
        }
    }
}
