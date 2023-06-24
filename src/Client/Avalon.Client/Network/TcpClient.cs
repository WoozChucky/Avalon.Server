using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Crypto;
using Avalon.Network.Packets.Deserialization;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.Serialization;
using ProtoBuf;

namespace Avalon.Client.Network;

public delegate void PlayerConnectedHandler(object? sender, SPlayerConnectedPacket packet);
public delegate void PlayerDisconnectedHandler(object? sender, SPlayerDisconnectedPacket packet);
public delegate void PlayerMovedHandler(object? sender, SPlayerPositionUpdatePacket packet);
public delegate void LatencyUpdated(object? sender, double latency);

public class TcpClient : IDisposable
{
    private static TcpClient instance;
    public static TcpClient Instance => instance ??= new TcpClient();
    
    public event PlayerConnectedHandler PlayerConnected;
    public event PlayerDisconnectedHandler PlayerDisconnected;
    public event PlayerMovedHandler PlayerMoved;

    private readonly CancellationTokenSource cts = new CancellationTokenSource();
    private readonly X509Certificate2 certificate;
    private readonly Socket socket;
    private SslStream stream;

    private readonly IPacketDeserializer _packetDeserializer;
    private readonly IPacketSerializer _packetSerializer;
    
    public TcpClient()
    {
        _packetDeserializer = new NetworkPacketDeserializer();
        _packetSerializer = new NetworkPacketSerializer();
        var clientCertBytes = File.ReadAllBytesAsync("cert-public.pem").ConfigureAwait(true).GetAwaiter().GetResult();
        certificate = new X509Certificate2(clientCertBytes);
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        
        _packetDeserializer.RegisterPacketDeserializers();
        _packetSerializer.RegisterPacketSerializers();
    }

    public async Task ConnectAsync()
    {
        await socket.ConnectAsync(new IPEndPoint(IPAddress.Parse("85.246.128.207"), 21000)).ConfigureAwait(true);
        stream = new SslStream(new NetworkStream(socket), false, UserCertificateValidationCallback);
        await stream.AuthenticateAsClientAsync("85.246.128.207", new X509Certificate2Collection() { certificate }, SslProtocols.Tls12,
            true).ConfigureAwait(false);
        
        await SendWelcomePacket();
        Task.Run(HandleCommunications);
    }

    private bool UserCertificateValidationCallback(object sender, X509Certificate? x509Certificate, X509Chain? chain, SslPolicyErrors sslpolicyerrors)
    {
        return true;
    }
    
    private async Task SendWelcomePacket()
    {
        var packet = CWelcomePacket.Create(Globals.ClientId);

        Serializer.SerializeWithLengthPrefix(stream, packet, PrefixStyle.Base128);
    }
    
    
    
    public async Task BroadcastMovementUpdates(float time, float x, float y, float velX, float velY)
    {
        try
        {
            var packet = CPlayerMovementPacket.Create(Globals.ClientId, time, x, y, velX, velY);

            await _packetSerializer.SerializeToNetwork(stream, packet);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
    
    private async Task HandleCommunications()
    {
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var packet = Serializer.DeserializeWithLengthPrefix<NetworkPacket>(stream, PrefixStyle.Base128);

                switch (packet.Header.Type)
                {
                    case NetworkPacketType.SMSG_ENCRYPTION_KEY:
                        var encryptionKeyPacket = Serializer.Deserialize<SCryptoKeyPacket>(new MemoryStream(packet.Payload));
                        break;
                    case NetworkPacketType.SMSG_PLAYER_CONNECTED:
                        var connectPacket = Serializer.Deserialize<SPlayerConnectedPacket>(new MemoryStream(packet.Payload));
                        PlayerConnected?.Invoke(this, connectPacket);
                        break;
                    case NetworkPacketType.SMSG_PLAYER_DISCONNECTED:
                        var disconnectPacket = Serializer.Deserialize<SPlayerDisconnectedPacket>(new MemoryStream(packet.Payload));
                        PlayerDisconnected?.Invoke(this, disconnectPacket);
                        break;
                    case NetworkPacketType.SMSG_PLAYER_POSITION_UPDATE:
                        var positionUpdatePacket = Serializer.Deserialize<SPlayerPositionUpdatePacket>(new MemoryStream(packet.Payload));
                        PlayerMoved?.Invoke(this, positionUpdatePacket);
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
    
    private async void ConnectToServer()
    {
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

        Serializer.SerializeWithLengthPrefix(stream, packet, PrefixStyle.Base128);
    }

    public void Dispose()
    {
        cts?.Dispose();
        certificate?.Dispose();
        socket?.Dispose();
        stream?.Dispose();
    }
}
