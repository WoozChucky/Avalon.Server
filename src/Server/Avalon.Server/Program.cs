using System.Diagnostics;
using System.Diagnostics.Tracing;
using Avalon.Database;
using Avalon.Game;
using Avalon.Game.Creatures;
using Avalon.Game.Handlers;
using Avalon.Game.Maps;
using Avalon.Game.Pools;
using Avalon.Game.Scripts;
using Avalon.Infrastructure;
using Avalon.Metrics;
using Avalon.Network;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Deserialization;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Tcp;
using Avalon.Network.Tcp.Configuration;
using Avalon.Network.Udp;
using Avalon.Network.Udp.Configuration;
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
        private static IAvalonInfrastructure Infrastructure { get; set; } = null!;
        private static ILogger<Program> Logger { get; set; } = null!;
        
        private static IMetricsManager MetricsManager { get; set; } = null!;
        
        sealed class GcFinalizersEventListener : EventListener
        {
            // from https://docs.microsoft.com/en-us/dotnet/framework/performance/garbage-collection-etw-events
            private const int GC_KEYWORD =                 0x0000001;
            private const int TYPE_KEYWORD =               0x0080000;
            private const int GCHEAPANDTYPENAMES_KEYWORD = 0x1000000;

            protected override void OnEventSourceCreated(EventSource eventSource)
            {
                Console.WriteLine($"{eventSource.Guid} | {eventSource.Name}");

                // look for .NET Garbage Collection events
                if (eventSource.Name.Equals("Microsoft-Windows-DotNETRuntime"))
                {
                    EnableEvents(
                        eventSource, 
                        EventLevel.Verbose, 
                        (EventKeywords) (GC_KEYWORD | GCHEAPANDTYPENAMES_KEYWORD | TYPE_KEYWORD)
                    );
                }
            }
            
            // Called whenever an event is written.
            protected override void OnEventWritten(EventWrittenEventArgs eventData)
            {
                // Write the contents of the event to the console.
                Console.WriteLine($"ThreadID = {eventData.OSThreadId} ID = {eventData.EventId} Name = {eventData.EventName}");
                for (int i = 0; i < eventData.Payload.Count; i++)
                {
                    string payloadString = eventData.Payload[i] != null ? eventData.Payload[i].ToString() : string.Empty;
                    Console.WriteLine($"    Name = \"{eventData.PayloadNames[i]}\" Value = \"{payloadString}\"");
                }
                Console.WriteLine("\n");
            }
        }

        private static Task Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledExceptionOccurred;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Console.CancelKeyPress += ConsoleOnCancelKeyPress;
            //AssemblyLoadContext.Default.Unloading += SigTermEventHandler;

            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.RealTime;
            Process.GetCurrentProcess().PriorityBoostEnabled = true;
            
            // new GcFinalizersEventListener();

            ConfigureDependencyInjection();

            MetricsManager.Start(new Dictionary<string, string>()
            {
                {"Host", Environment.MachineName},
                {"OS", Environment.OSVersion.VersionString},
                {"SystemPageSize", Environment.SystemPageSize.ToString()},
                {"ProcessorCount", Environment.ProcessorCount.ToString()},
                {"UserDomainName", Environment.UserDomainName},
                {"UserName", Environment.UserName},
                {"Version", Environment.Version.ToString()},
                {"WorkingSet", Environment.WorkingSet.ToString()},
                {"Application", "Avalon.Server"},
            });

            Infrastructure.Start();
            
            while (!CancellationTokenSource.IsCancellationRequested)
            {
                Infrastructure.Loop(10);
            }
            
            Logger.LogInformation("Stopping application...");
            
            Infrastructure.Stop();
            Infrastructure.Dispose();
            
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

        private static void ConfigureDependencyInjection()
        {
            ServiceProvider = new ServiceCollection()
                .AddLogging(builder =>
                {
                    builder
                        .AddSerilog(new LoggerConfiguration()
                            .MinimumLevel.Verbose()
                            .Enrich.FromLogContext()
                            .Enrich.WithThreadId()
                            .Enrich.WithThreadName()
                            .WriteTo.Console(LogEventLevel.Debug, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] [{ThreadId}] ({SourceContext}) -> {Message}{NewLine}{Exception}", theme: AnsiConsoleTheme.Sixteen)
                            .CreateLogger()
                        )
                        .SetMinimumLevel(LogLevel.Trace);
                })
                .AddSingleton<AvalonUdpServerConfiguration>(_ => new AvalonUdpServerConfiguration
                {
                    Backlog = 32,
                    CertificatePath = "cert-server-udp.pem",
                    ListenPort = 21000
                })
                .AddSingleton<AvalonTcpServerConfiguration>(_ => new AvalonTcpServerConfiguration
                {
                    Backlog = 32,
                    CertificatePassword = "avalon",
                    CertificatePath = "cert-tcp.pfx",
                    ListenPort = 21000
                })
                .AddSingleton<MetricsConfiguration>(_ => new MetricsConfiguration()
                {
                    ApiUrl = "https://portal-api.dev.quix.ai",
                    ApiKey = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCIsImtpZCI6IlpXeUJqWTgzcXotZW1pUlZDd1I4dyJ9.eyJodHRwczovL3F1aXguYWkvb3JnX2lkIjoicXVpeGRldiIsImh0dHBzOi8vcXVpeC5haS9vd25lcl9pZCI6ImF1dGgwfDRiY2RlODU5LTA3OWUtNDE4Yi04NTQ3LTE2ZjFkMWYwZjYwNiIsImh0dHBzOi8vcXVpeC5haS90b2tlbl9pZCI6ImMyNGZjMzUyLWJmMDQtNGExOC1hZDBmLTcyNDEzYzA0NTFlNiIsImh0dHBzOi8vcXVpeC5haS9leHAiOiIyNDEwODE1NjAwIiwiaHR0cHM6Ly9xdWl4LmFpL3JvbGVzIjoiUXVpeEFkbWluIGFkbWluIiwiaXNzIjoiaHR0cHM6Ly9hdXRoLmRldi5xdWl4LmFpLyIsInN1YiI6IkFvcUJJVGFzeFhucHNWQ1BNY1FMUFk4OEJjZXd0d3g4QGNsaWVudHMiLCJhdWQiOiJodHRwczovL3BvcnRhbC1hcGkuZGV2LnF1aXguYWkvIiwiaWF0IjoxNjg0OTc0NTgwLCJleHAiOjE2ODc1NjY1ODAsImF6cCI6IkFvcUJJVGFzeFhucHNWQ1BNY1FMUFk4OEJjZXd0d3g4IiwiZ3R5IjoiY2xpZW50LWNyZWRlbnRpYWxzIiwicGVybWlzc2lvbnMiOltdfQ.Au2P2iklX3yFQ3ALNEA_Hqnjl2UoPm_usXkuTo3-D8s4nk3K0vP5_e6D4lcAhGc4iBLkVBxrZOblESxhjDqEnpYHv5u1OvLzsS57VVzTsxfy2YstxifttfLeC1lGhv04sa0HuOXmTwv94X_2RDpjSN5hHM6kSS6FYqvZqaygOsY2lM9cGCRlASwo0apTaV9B1vbU8M6bGLgTAWOu82jWqxCoA11Sj7B4TKsLMI7kHvDP42E6WMwCz0cWhaHtI1CWvTbD15henDDtG_Y0kXY8HHxUG27177xq3JYJ9cQsyqO13kncC3DHfF-RxGKBKoO2SbZgGe73TTw2VTw16QJJ8Q",
                    Automatic = true
                })
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
                .AddSingleton<IAvalonGame, AvalonGame>()
                .AddSingleton<IAvalonInfrastructure, AvalonInfrastructure>()
                .AddSingleton<CancellationTokenSource>(s => new CancellationTokenSource())
                .BuildServiceProvider();
            
            Logger = ServiceProvider.GetService<ILogger<Program>>() ?? throw new InvalidOperationException();
            Infrastructure = ServiceProvider.GetService<IAvalonInfrastructure>() ?? throw new InvalidOperationException();
            CancellationTokenSource = ServiceProvider.GetService<CancellationTokenSource>() ?? throw new InvalidOperationException();
            MetricsManager = ServiceProvider.GetService<IMetricsManager>() ?? throw new InvalidOperationException();
        }
    }
}
