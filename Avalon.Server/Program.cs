using System.Net;
using System.Security.Cryptography.X509Certificates;
using DtlsServer = Avalon.Network.Security.Udp.Server;

namespace Avalon.Server
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            Console.CancelKeyPress += ConsoleOnCancelKeyPress;
            
            
            
            var serverCertBytes = await File.ReadAllBytesAsync("cert.pfx");
            X509Certificate2 serverCertificate = new X509Certificate2(serverCertBytes, "avalon");

            await using var fs = new FileStream("cert-server-udp.pem", FileMode.Open);
            
            
            
            DtlsServer server = new DtlsServer(new IPEndPoint(IPAddress.Any, 21000));
            server.LoadCertificateFromPem(fs);
            server.Start();
        }

        private static void ConsoleOnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            Console.WriteLine("Canceling...");
        }
    }
}
