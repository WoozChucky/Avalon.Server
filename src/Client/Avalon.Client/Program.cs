using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalon.Client;
using Mono.Nat;

namespace Avalon.Client
{
    internal class Program
    {
        private static Guid ClientId;
        
        
        private static async Task Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

            if (File.Exists("av.bin"))
            {
                var text = await File.ReadAllTextAsync("av.bin");
                ClientId = Guid.Parse(text);
            }
            else
            {
                ClientId = Guid.NewGuid();
                //await File.WriteAllTextAsync("av.bin", ClientId.ToString());
            }
            
            NatUtility.DeviceFound += async (sender, args) =>
            {
                Console.WriteLine($"Found NAT device: {args.Device}");

                var existingMappings = await args.Device.GetAllMappingsAsync();
                
                if (existingMappings.Any(m => m.PrivatePort == 21000 && m.PublicPort == 21000))
                {
                    Console.WriteLine("Port already mapped.");
                    return;
                }

                var mappedPort = args.Device.CreatePortMap(new Mapping(Protocol.Udp, 21000, 21000, 0, "AvalonClient"));
                if (mappedPort != null)
                    Console.WriteLine($"Mapped port {mappedPort.PublicPort} to {mappedPort.PrivatePort}.");
                else
                    Console.WriteLine("Failed to map port.");
                //args.Device.
            };
            NatUtility.StartDiscovery();
            
            using var game = new AvalonGame(ClientId);
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
