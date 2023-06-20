using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Crypto;
using ProtoBuf;

namespace Avalon.Client.Tester;

public partial class TcpForm : Form
{
    private readonly CancellationTokenSource cts = new CancellationTokenSource();
    private readonly X509Certificate2 certificate;
    private readonly Socket socket;
    private SslStream sslStream;


    public TcpForm()
    {
        InitializeComponent();
        this.FormClosing += Form1_FormClosing;

        // Load the certificate from file
        var clientCertBytes = File.ReadAllBytesAsync("cert-public.pem").ConfigureAwait(true).GetAwaiter().GetResult();
        certificate = new X509Certificate2(clientCertBytes);
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    }

    private void Form1_Load(object sender, EventArgs e)
    {

    }

    private async void Form1_FormClosing(object? sender, FormClosingEventArgs e)
    {
        cts.Cancel();
        await sslStream.DisposeAsync();
    }

    private async void button1_Click(object sender, EventArgs e)
    {
        await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, 21000));
        sslStream = new SslStream(new NetworkStream(socket), false, UserCertificateValidationCallback);
        await sslStream.AuthenticateAsClientAsync("localhost", new X509Certificate2Collection() { certificate }, SslProtocols.Tls12,
            true);

        Task.Run(HandleCommunications, cts.Token);

        var reqPKeyPacket = new CRequestCryptoKeyPacket();
        using var ms = new MemoryStream();

        Serializer.Serialize(ms, reqPKeyPacket);

        var packet = new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = NetworkPacketType.CMSG_REQUEST_ENCRYPTION_KEY,
                Flags = NetworkPacketFlags.None,
                Version = 0
            },
            Payload = ms.ToArray()
        };

        Serializer.SerializeWithLengthPrefix(sslStream, packet, PrefixStyle.Base128);
    }


    private void button2_Click(object sender, EventArgs e)
    {

    }

    private void button3_Click(object sender, EventArgs e)
    {

    }

    private bool UserCertificateValidationCallback(object sender, X509Certificate? x509Certificate, X509Chain? chain, SslPolicyErrors sslpolicyerrors)
    {

        return true;
    }

    private async Task HandleCommunications()
    {
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var packet = Serializer.DeserializeWithLengthPrefix<NetworkPacket>(sslStream, PrefixStyle.Base128);

                switch (packet.Header.Type)
                {
                    case NetworkPacketType.SMSG_ENCRYPTION_KEY:
                        var encryptionKeyPacket = Serializer.Deserialize<SCryptoKeyPacket>(new MemoryStream(packet.Payload));
                        textBox1.Text = Encoding.UTF8.GetString(encryptionKeyPacket.Key);
                        break;
                }
            }
        }
        catch (OperationCanceledException e)
        {

        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
}
