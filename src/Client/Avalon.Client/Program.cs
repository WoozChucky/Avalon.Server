using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalon.Client;
using Mono.Nat;

/*
NatUtility.DeviceFound += (sender, args) =>
{
    Console.WriteLine($"Found NAT device: {args.Device}");

    var mappedPort = args.Device.CreatePortMap(new Mapping(Protocol.Udp, 8889, 8889, 0, "AvalonClient"));
    if (mappedPort != null)
        Console.WriteLine($"Mapped port {mappedPort.PublicPort} to {mappedPort.PrivatePort}.");
    else
        Console.WriteLine("Failed to map port.");
    //args.Device.
};
NatUtility.StartDiscovery();
*/



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
                await File.WriteAllTextAsync("av.bin", ClientId.ToString());
            }
            
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
