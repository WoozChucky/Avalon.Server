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
using Avalon.Common.Extensions;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Crypto;
using Avalon.Network.Packets.Movement;
using ProtoBuf;

namespace Avalon.Client.Network;

public class UdpClient : IDisposable
{
    private static UdpClient instance;
    
    public static UdpClient Instance => instance ??= new UdpClient();
    
    private readonly CancellationTokenSource cts = new CancellationTokenSource();
    private readonly Socket socket;
    private IPEndPoint serverEndpoint;
    
    
    public UdpClient()
    {
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    }

    public async Task ConnectAsync()
    {
        var localEndpoint = IPEndPoint.Parse("127.0.0.1");
        socket.Bind(localEndpoint);
        
        serverEndpoint = IPEndPoint.Parse("127.0.0.1");
        serverEndpoint.Port = 21500;

        await SendWelcomePacket();

        Task.Run(HandleCommunications);
    }

    private async Task SendWelcomePacket()
    {
        using var buffer = new MemoryStream();
            
        var packet = CWelcomePacket.Create(Globals.ClientId);

        Serializer.SerializeWithLengthPrefix(buffer, packet, PrefixStyle.Base128);
            
        await socket.SendToAsync(buffer.ToArray(), SocketFlags.None, serverEndpoint);
    }

    public async Task BroadcastMovementUpdates(float time, float x, float y)
    {
        try
        {
            using var buffer = new MemoryStream();
            
            var packet = CPlayerMovementPacket.Create(Globals.ClientId, time, x, y, 0f, 0f);

            Serializer.SerializeWithLengthPrefix(buffer, packet, PrefixStyle.Base128);
            
            await socket.SendToAsync(buffer.ToArray(), SocketFlags.None, serverEndpoint);
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
            var readBuffer = new byte[1024];
            var endpoint = new IPEndPoint(IPAddress.Any, 0);
                    
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    var result = await socket.ReceiveFromAsync(readBuffer, SocketFlags.None, endpoint);
                    var packetBuffer = new byte[result.ReceivedBytes];
                
                    Array.Copy(readBuffer, packetBuffer, result.ReceivedBytes);
                
                    await using var ms = new MemoryStream(packetBuffer);
                        
                    var packet = Serializer.DeserializeWithLengthPrefix<NetworkPacket>(ms, PrefixStyle.None);

                    switch (packet.Header.Type)
                    {
                        case NetworkPacketType.SMSG_PLAYER_POSITION_UPDATE:
                        {
                            var movementPacket = Serializer.DeserializeWithLengthPrefix<SPlayerPositionUpdatePacket>(packet.Payload.ToMemoryStream(), PrefixStyle.Base128);
                            Trace.WriteLine($"Received movement update from {movementPacket.ClientId} ({movementPacket.PositionX}, {movementPacket.PositionY})\r\n");
                            break;
                        } 
                    }
                }
                catch (Exception e)
                {
                   
                }
            }
        }
        catch (OperationCanceledException e)
        {
            Trace.WriteLine(e);
        }
    }
    
    public void Dispose()
    {
        cts?.Dispose();
        socket?.Dispose();
    }
}
