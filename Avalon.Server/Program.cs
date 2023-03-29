using Avalon.Network;
using Avalon.Network.Packets.Deserialization;
using Avalon.Network.Tcp;
using Avalon.Network.Tcp.Configuration;
using Avalon.Network.Udp;
using Avalon.Network.Udp.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

//using DtlsClient = Avalon.Network.Security.Udp.Client;

namespace Avalon.Server
{
    internal class Program
    {
        private static IServiceProvider ServiceProvider { get; set; } = null!;
        private static AvalonInfrastructure Infrastructure { get; set; } = null!;
        private static ILogger<Program> Logger { get; set; } = null!;

        private static async Task Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledExceptionOccurred;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
            Console.CancelKeyPress += ConsoleOnCancelKeyPress;
            
            ConfigureDependencyInjection();
            
            await Infrastructure.Run().ConfigureAwait(true);
        }

        private static async void ConsoleOnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            Logger.LogInformation("Cancelling...");
            await Infrastructure.GracefulStop().ConfigureAwait(true);
            Infrastructure.Dispose();
            Logger.LogInformation("Cancelled");
        }
        
        private static void OnUnhandledExceptionOccurred(object sender, UnhandledExceptionEventArgs e)
        {
            Logger.LogError(e.ExceptionObject as Exception, "Unhandled exception");
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
                        .AddSimpleConsole(options =>
                        {
                            options.IncludeScopes = true;
                            options.SingleLine = false;
                            options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
                        })
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
                .AddSingleton<AvalonGame>()
                .AddSingleton<AvalonInfrastructure>()
                .BuildServiceProvider();
            
            Logger = ServiceProvider.GetService<ILogger<Program>>() ?? throw new InvalidOperationException();
            Infrastructure = ServiceProvider.GetService<AvalonInfrastructure>() ?? throw new InvalidOperationException();
        }
    }
}
