using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Avalon.Network.Packets;
using ProtoBuf;

namespace Avalon.Client
{
    internal class Program
    {
        static CancellationTokenSource cts = new CancellationTokenSource();
        
        static async Task Main(string[] args)
        {
            Console.CancelKeyPress += ConsoleOnCancelKeyPress;

            // Load the certificate from file
            var clientCertBytes = await File.ReadAllBytesAsync("cert-public.pem");
            var clientCert = new X509Certificate2(clientCertBytes);
            
            // Connect to the server using a socket
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 21000));
            
            // Create an SslStream over the socket
            var sslStream = new SslStream(new NetworkStream(socket), false, UserCertificateValidationCallback);
            await sslStream.AuthenticateAsClientAsync("localhost", new X509Certificate2Collection() { clientCert }, SslProtocols.Tls12,
                true);

            // Send a message to the server
            var message = "Hello, server!|exit";
            var buffer = Encoding.UTF8.GetBytes(message);
            await sslStream.WriteAsync(buffer, cts.Token);
            
            while (!cts.IsCancellationRequested)
            {
                // Receive a response from the server
                buffer = new byte[4096];
                using var ms = new MemoryStream();
                //var bytesRead = await sslStream.ReadAsync(buffer, cts.Token);
                //await ms.WriteAsync(buffer, 0, bytesRead, cts.Token);
                var obj = Serializer.DeserializeWithLengthPrefix<UserPacket>(sslStream, PrefixStyle.Base128);
                if (obj is UserPacket)
                {
                    
                }
                //var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                //Console.WriteLine(response);
            }
            
            // Close the stream and socket
            sslStream.Close();
            socket.Close();
            
        }

        private static bool UserCertificateValidationCallback(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslpolicyerrors)
        {
            return true;
        }

        private static void ConsoleOnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            cts.Cancel();
        }
    }
}
