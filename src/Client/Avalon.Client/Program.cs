using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Mono.Nat;
using Steamworks;


namespace Avalon.Client
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

            SteamClient.Init(2499460);

            NatUtility.DeviceFound += async (_, natArgs) =>
            {
                var existingMappings = await natArgs.Device.GetAllMappingsAsync();
                
                if (existingMappings.Any(m => m.PrivatePort == 21500 && m.PublicPort == 21500))
                {
                    return;
                }

                var mappedPort = natArgs.Device.CreatePortMap(new Mapping(Protocol.Udp, 21500, 21500, 0, "AvalonClient"));
                Console.WriteLine(mappedPort != null
                    ? $"Mapped port {mappedPort.PublicPort}."
                    : "Failed to map port.");
                NatUtility.StopDiscovery();
            };
            NatUtility.StartDiscovery();
            
            using var game = new AvalonGame();
            game.Run();
        }

        private static void OnProcessExit(object sender, EventArgs e)
        {
            Console.WriteLine("Process exit");
        }

        private static void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = e.ExceptionObject as Exception;

            File.WriteAllText("crash.txt", ex + "\n\n" + ex?.StackTrace ?? "Unknown exception");
        }
    }
}
