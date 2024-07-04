using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Microsoft.Extensions.Logging;

namespace Avalon.Hosting.Auth;

public class AuthConnection : Connection, IAuthConnection
{
    public int? AccountId { get; set; }

    private byte[] _handshakeData = [];
    
    public AuthConnection(IServerBase server, TcpClient client, ILoggerFactory loggerFactory, 
        PluginExecutor pluginExecutor, IPacketReader packetReader) 
        : base(loggerFactory.CreateLogger<AuthConnection>(), server, pluginExecutor, packetReader)
    {
        Init(client);
    }

    protected override void OnHandshakeFinished()
    {
        Server.CallConnectionListener(this);
    }
    
    public byte[] GenerateHandshakeData()
    {
        _handshakeData = CryptoSession.GenerateHandshakeData();
        return _handshakeData;
    }

    public bool VerifyHandshakeData(byte[] handshakeData)
    {
        return _handshakeData.SequenceEqual(handshakeData);
    }

    protected override async Task<Stream> GetStream(TcpClient client)
    {
        var networkStream = new NetworkStream(client.Client);
        var sslStream = new SslStream(networkStream, false, OnClientCertificateValidation);

        var certificate = (Server as AuthServer)!.Certificate;
        
        await sslStream.AuthenticateAsServerAsync(certificate, false, SslProtocols.Tls12, false);
        
        return sslStream;
    }

    protected override async Task OnClose(bool expected = true)
    {
        await Server.RemoveConnection(this);
    }

    protected override async Task OnReceive(NetworkPacket packet, Packet? payload)
    {
        await Server.CallListener(this, packet, payload);
    }

    protected override long GetServerTime()
    {
        return Server.ServerTime;
    }
    
    private bool OnClientCertificateValidation(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslpolicyerrors)
    {
        return true;
    }
}
