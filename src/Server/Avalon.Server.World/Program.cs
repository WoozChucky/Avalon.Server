using System.Diagnostics;
using System.Reflection;
using Avalon.Common.Telemetry;
using Avalon.Database;
using Avalon.Database.Extensions;
using Avalon.Game;
using Avalon.Game.Creatures;
using Avalon.Game.Maps;
using Avalon.Game.Pools;
using Avalon.Game.Quests;
using Avalon.Game.Scripts;
using Avalon.Infrastructure;
using Avalon.Metrics;
using Avalon.Network;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
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
using ProtoBuf;
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
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledExceptionOccurred;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Console.CancelKeyPress += ConsoleOnCancelKeyPress;
            //AssemblyLoadContext.Default.Unloading += SigTermEventHandler;
            
            // Get all types that inherit from Packet
            var packetTypes = Assembly.GetAssembly(typeof(Packet))?.GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(Packet)))
                .ToList() ?? new List<Type>();
            
            // Serializer.GetProto<T>() generates a proto definition for a given type
            // So we need to use this method via reflection to generate proto definitions for all packet types
            // Get the method named "GetProto" that returns a string
            MethodInfo getProtoMethod = typeof(Serializer).GetMethods()
                .FirstOrDefault(m => m.Name == "GetProto" && m.ReturnType == typeof(string) && m.IsGenericMethod);
            
            // Generate proto definitions for all packet types
            // Save each proto definition to a file
            foreach (var packetType in packetTypes)
            {
                // Construct the Serializer.GetProto<T>() method using reflection
                MethodInfo genericGetProtoMethod = getProtoMethod.MakeGenericMethod(packetType);
                
                // Invoke the generic method to get the proto definition
                var proto = (string)genericGetProtoMethod.Invoke(null, null);

                var filePath = $"./proto/{packetType.Name}.proto";
                File.WriteAllText(filePath, proto);
                Console.WriteLine($"Proto definition for {packetType.Name} saved to {filePath}");
            }

            File.WriteAllText($"./proto/network-packet.proto", Serializer.GetProto<NetworkPacket>());
            
            //var proto = Serializer.GetProto<CRequestServerInfoPacket>();

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
            services.AddSingleton(AppConfiguration.Infrastructure!);
            services.AddSingleton(AppConfiguration.NetworkDaemon!);
            services.AddSingleton(AppConfiguration.NetworkDaemon!.Udp!);
            services.AddSingleton(AppConfiguration.NetworkDaemon.Tcp!);
            services.AddSingleton(AppConfiguration.Metrics);
            services.AddSingleton(AppConfiguration.Database!);
            services.AddSingleton(AppConfiguration.Game!);

            if (AppConfiguration.Metrics.Enabled)
            {
                services.AddSingleton<IMetricsManager, MetricsManager>();
            }
            else
            {
                services.AddSingleton<IMetricsManager, FakeMetricsManager>();
            }

            services.AddDatabases(AppConfiguration.Database!);
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
        }
    }
}
