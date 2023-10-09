using System.Diagnostics;
using System.Reflection;
using Avalon.Database;
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
using Avalon.Network.Tcp.Configuration;
using Avalon.Network.Udp;
using Avalon.Network.Udp.Configuration;
using Avalon.Server.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        private static AllocationsListener AllocationsListener { get; set; } = null!;
        private static SystemUsageCollector SystemUsageCollector { get; set; } = null!;

        private static Task Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledExceptionOccurred;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Console.CancelKeyPress += ConsoleOnCancelKeyPress;
            //AssemblyLoadContext.Default.Unloading += SigTermEventHandler;

            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.RealTime;
            Process.GetCurrentProcess().PriorityBoostEnabled = true;

            ConfigureConfiguration(args);
            ConfigureDependencyInjection();
            
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
            
            return Task.CompletedTask;
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
                .AddEnvironmentVariables()
                .AddJsonFile("appsettings.json", false, false);
            
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
            ServiceProvider = new ServiceCollection()
                
                // Logging
                .AddLogging(builder =>
                {
                    builder
                        .AddSerilog(new LoggerConfiguration()
                            .MinimumLevel.Verbose()
                            .Enrich.FromLogContext()
                            .Enrich.WithThreadId()
                            .WriteTo.Console(LogEventLevel.Debug, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] [{ThreadId}] ({SourceContext}) -> {Message}{NewLine}{Exception}", theme: AnsiConsoleTheme.Sixteen)
                            .CreateLogger()
                        )
                        .SetMinimumLevel(LogLevel.Trace);
                })
                
                // Configuration
                .AddSingleton(AppConfiguration)
                .AddSingleton(AppConfiguration.NetworkDaemon)
                .AddSingleton(AppConfiguration.NetworkDaemon.Udp)
                .AddSingleton(AppConfiguration.NetworkDaemon.Tcp)
                .AddSingleton(AppConfiguration.Metrics)
                
                .AddSingleton<AllocationsListener>()
                .AddSingleton<SystemUsageCollector>()
                .AddSingleton<IDatabaseManager, DatabaseManager>()
                .AddSingleton<IMetricsManager, MetricsManager>()
                .AddSingleton<IAvalonTcpServer, AvalonTcpServer>()
                .AddSingleton<IAvalonUdpServer, ENetUdpServer>()
                .AddSingleton<IAvalonConnectionManager, AvalonConnectionManager>()
                .AddSingleton<IPacketDeserializer, NetworkPacketDeserializer>()
                .AddSingleton<IPacketSerializer, NetworkPacketSerializer>()
                .AddSingleton<IPacketRegistry, PacketRegistry>()
                .AddSingleton<IAvalonNetworkDaemon, AvalonNetworkDaemon>()
                .AddSingleton<IAvalonMapManager, AvalonMapManager>()
                .AddSingleton<ICreatureSpawner, CreatureSpawner>()
                .AddSingleton<IPoolManager, PoolManager>()
                .AddSingleton<IAIController, AIController>()
                .AddSingleton<IQuestManager, QuestManager>()
                .AddSingleton<IAvalonGame, AvalonGame>()
                .AddSingleton<ICryptoManager, CryptoManager>()
                .AddSingleton<IAvalonInfrastructure, AvalonInfrastructure>()
                .AddSingleton<CancellationTokenSource>(s => new CancellationTokenSource())
                .BuildServiceProvider();
            
            Logger = ServiceProvider.GetService<ILogger<Program>>() ?? throw new InvalidOperationException();
            Infrastructure = ServiceProvider.GetService<IAvalonInfrastructure>() ?? throw new InvalidOperationException();
            CancellationTokenSource = ServiceProvider.GetService<CancellationTokenSource>() ?? throw new InvalidOperationException();
            MetricsManager = ServiceProvider.GetService<IMetricsManager>() ?? throw new InvalidOperationException();
            AllocationsListener = ServiceProvider.GetService<AllocationsListener>() ?? throw new InvalidOperationException();
            SystemUsageCollector = ServiceProvider.GetService<SystemUsageCollector>() ?? throw new InvalidOperationException();
        }
    }
}
