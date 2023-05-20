using System.Diagnostics;
using Avalon.Game;
using Avalon.Game.Handlers;
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
using QuixStreams.Streaming;
using QuixStreams.Streaming.Models;
using QuixStreams.Telemetry.Models;
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

            var token = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCIsImtpZCI6IlpXeUJqWTgzcXotZW1pUlZDd1I4dyJ9.eyJodHRwczovL3F1aXguYWkvb3JnX2lkIjoiYmlsbGluZzEiLCJodHRwczovL3F1aXguYWkvb3duZXJfaWQiOiJhdXRoMHw5ZGMzYmJiZS0yODMyLTRiOGYtOTgxZC0wMzJjNzA4MmRjODUiLCJodHRwczovL3F1aXguYWkvdG9rZW5faWQiOiJiZDZiMDk3Ni05OTIxLTRkYTEtYjFhNC05ODIxM2Y2YWMyZjYiLCJodHRwczovL3F1aXguYWkvZXhwIjoiMTY4NTQ4NzYwMCIsImh0dHBzOi8vcXVpeC5haS9yb2xlcyI6ImFkbWluIiwiaXNzIjoiaHR0cHM6Ly9hdXRoLmRldi5xdWl4LmFpLyIsInN1YiI6InM2b3kyNGc2bmZJMjlnR0Y3VVMxOHpGTjlxdUc3ZmRCQGNsaWVudHMiLCJhdWQiOiJodHRwczovL3BvcnRhbC1hcGkuZGV2LnF1aXguYWkvIiwiaWF0IjoxNjg0NTI5OTQxLCJleHAiOjE2ODcxMjE5NDEsImF6cCI6InM2b3kyNGc2bmZJMjlnR0Y3VVMxOHpGTjlxdUc3ZmRCIiwiZ3R5IjoiY2xpZW50LWNyZWRlbnRpYWxzIiwicGVybWlzc2lvbnMiOltdfQ.QKKO_z5sbNwZuslAJnASPNLn9VBT9UVcXSDQfh2QR4b-MCozkugo8N7fL1NUibe1BDQc3xvsFWiFAAsRRoLdn_IECmzLKn9IjqykVPHeN_tIJQfXsvnovLPAj4DSoTNVLmC84qGLnX-42pwI3KKBgYWLGEbfIXh9aLZxLDMl_8caNcPqjokja4vehbZyVRR3QN32B4oWlptlMT9fhHUi79u-Zoe2eDk6fQpVWYB8lti9YXesYjj78moX2Jw_92rygCIBDNjfoTNUXqmnEtXmnzyOcpGLrcki6HFQ1UMABPoB-n93KY9rFnT3KVrL1cqnJD-QX6XkYqRlRcF9i9hdfQ";
            
            var quix = new QuixStreamingClient(token, true, null, false, null);
            quix.ApiUrl = new Uri("https://portal-api.dev.quix.ai");
            var producer = quix.GetTopicProducer("f1-data");
            var streamProducer = producer.CreateStream();

            var timeseriesData = new TimeseriesData();
            timeseriesData.AddTimestamp(DateTime.UtcNow).AddValue("Id", 1).AddTag("IdTag", "IdTagValue");

            var eventData = new EventData("MyEvent", DateTime.UtcNow, "EventValue1");
            eventData.AddTag("TheTag1", "TheTagValue1");
            
            
            
            var consumer = quix.GetTopicConsumer("f1-data");
            consumer.OnStreamReceived += (sender, streamConsumer) =>
            {
                streamConsumer.Events.OnDataReceived += (o, eventArgs) =>
                {
                    if (eventArgs != null)
                    {
                        
                    }
                };
                
                streamConsumer.Timeseries.OnDataReceived += (o, eventArgs) =>
                {
                    if (eventArgs != null)
                    {
                        
                    }
                };
            };
            consumer.Subscribe();
            
            streamProducer.Timeseries.Publish(timeseriesData);
            streamProducer.Events.Publish(eventData);
            
            streamProducer.Close();
            streamProducer.Dispose();

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
                .AddSingleton<IAvalonMovementManager, AvalonMovementManager>()
                .AddSingleton<IAvalonInfrastructure, AvalonInfrastructure>()
                .AddSingleton<CancellationTokenSource>(s => new CancellationTokenSource())
                .BuildServiceProvider();
            
            Logger = ServiceProvider.GetService<ILogger<Program>>() ?? throw new InvalidOperationException();
            Infrastructure = ServiceProvider.GetService<IAvalonInfrastructure>() ?? throw new InvalidOperationException();
            CancellationTokenSource = ServiceProvider.GetService<CancellationTokenSource>() ?? throw new InvalidOperationException();
        }
    }
}
