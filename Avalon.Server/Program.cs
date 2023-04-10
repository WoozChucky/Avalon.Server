using System.Diagnostics;
using Avalon.Game;
using Avalon.Infrastructure;
using Avalon.Network;
using Avalon.Network.Packets;
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

//using DtlsClient = Avalon.Network.Security.Udp.Client;

namespace Avalon.Server
{
    internal class Program
    {
        private static CancellationTokenSource CancellationTokenSource { get; set; } = null!;
        private static IServiceProvider ServiceProvider { get; set; } = null!;
        private static IAvalonInfrastructure Infrastructure { get; set; } = null!;
        private static ILogger<Program> Logger { get; set; } = null!;

        private static async Task Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledExceptionOccurred;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Console.CancelKeyPress += ConsoleOnCancelKeyPress;
            //AssemblyLoadContext.Default.Unloading += SigTermEventHandler;

            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
            
            ConfigureDependencyInjection();
            
            await Infrastructure.StartAsync().ConfigureAwait(true);
            
            while (!CancellationTokenSource.IsCancellationRequested)
            {
                
            }
            
            Logger.LogInformation("Stopping application...");
            
            await Infrastructure.StopAsync().ConfigureAwait(true);
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

        private static void ConfigureDependencyInjection()
        {
            ServiceProvider = new ServiceCollection()
                .AddLogging(builder =>
                {
                    builder
                        /*
                        .AddSimpleConsole(options =>
                        {
                            options.IncludeScopes = true;
                            options.SingleLine = false;
                            options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
                        })
                        */
                        .AddSerilog(new LoggerConfiguration()
                            .MinimumLevel.Verbose()
                            .Enrich.FromLogContext()
                            .WriteTo.Console(LogEventLevel.Debug, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level}] ({SourceContext}) -> {Message}{NewLine}{Exception}", theme: AnsiConsoleTheme.Sixteen)
                            .CreateLogger()
                        )
                        .SetMinimumLevel(LogLevel.Trace);
                })
                .AddSingleton<AvalonUdpServerConfiguration>(_ => new AvalonUdpServerConfiguration
                {
                    Backlog = 100,
                    CertificatePath = "cert-server-udp.pem",
                    ListenPort = 21500
                })
                .AddSingleton<AvalonTcpServerConfiguration>(_ => new AvalonTcpServerConfiguration
                {
                    Backlog = 100,
                    CertificatePassword = "avalon",
                    CertificatePath = "cert-tcp.pfx",
                    ListenPort = 21000
                })
                .AddSingleton<IAvalonTcpServer, AvalonTcpServer>()
                .AddSingleton<IAvalonUdpServer, AvalonUdpServer>()
                .AddSingleton<IPacketDeserializer, NetworkPacketDeserializer>()
                .AddSingleton<IPacketSerializer, NetworkPacketSerializer>()
                .AddSingleton<IPacketHandlerRegistry, PacketHandlerRegistry>()
                .AddSingleton<AvalonGame>()
                .AddSingleton<IAvalonInfrastructure, AvalonInfrastructure>()
                .AddSingleton<CancellationTokenSource>(s => new CancellationTokenSource())
                .BuildServiceProvider();
            
            Logger = ServiceProvider.GetService<ILogger<Program>>() ?? throw new InvalidOperationException();
            Infrastructure = ServiceProvider.GetService<IAvalonInfrastructure>() ?? throw new InvalidOperationException();
            CancellationTokenSource = ServiceProvider.GetService<CancellationTokenSource>() ?? throw new InvalidOperationException();
        }
    }
}
