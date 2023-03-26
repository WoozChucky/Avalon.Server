using System.Net;
using DtlsServer = Avalon.Network.Security.Udp.Server;
using DtlsClient = Avalon.Network.Security.Udp.Client;

namespace Avalon.Server
{
    internal class Program
    {
        static AvalonInfrastructure Infrastructure { get; } = new AvalonInfrastructure();
        
        static async Task Main(string[] args)
        {
            Console.CancelKeyPress += ConsoleOnCancelKeyPress;

            /*
            await using var fs = new FileStream("cert-server-udp.pem", FileMode.Open);
            DtlsServer server = new DtlsServer(new IPEndPoint(IPAddress.Any, 21000));
            server.LoadCertificateFromPem(fs);
            server.Start();
            */
            await Infrastructure.Run();
        }

        private static void ConsoleOnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            Console.WriteLine("Canceling...");
            Infrastructure.Dispose();
            Console.WriteLine("Cancelled...");
        }
    }
}
