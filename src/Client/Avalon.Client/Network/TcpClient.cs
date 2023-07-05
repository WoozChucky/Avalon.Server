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
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Crypto;
using Avalon.Network.Packets.Deserialization;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Social;
using ProtoBuf;

namespace Avalon.Client.Network;

public delegate void PlayerConnectedHandler(object sender, SPlayerConnectedPacket packet);
public delegate void PlayerDisconnectedHandler(object sender, SPlayerDisconnectedPacket packet);
public delegate void PlayerMovedHandler(object sender, SPlayerPositionUpdatePacket packet);
public delegate void LatencyUpdatedHandler(object sender, double latency);
public delegate void NpcUpdatedHandler(object sender, SNpcUpdatePacket packet);
public delegate void ChatMessageHandler(object sender, SChatMessagePacket packet);
public delegate void AuthResultHandler(object sender, SAuthResultPacket packet);
public delegate void GroupInviteHandler(object sender, SGroupInvitePacket packet);
public delegate void GroupResultHandler(object sender, SGroupResultPacket packet);
public delegate void CharacterListHandler(object sender, SCharacterListPacket packet);
public delegate void CharacterSelectedHandler(object sender, SCharacterSelectedPacket packet);
public delegate void CharacterCreatedHandler(object sender, SCharacterCreatedPacket packet);
public delegate void CharacterDeletedHandler(object sender, SCharacterDeletedPacket packet);

public class TcpClient : IDisposable
{
    private static TcpClient instance;
    public static TcpClient Instance => instance ??= new TcpClient();
    
    public event PlayerConnectedHandler PlayerConnected;
    public event PlayerDisconnectedHandler PlayerDisconnected;
    public event ChatMessageHandler ChatMessage;
    public event AuthResultHandler AuthResult;
    public event GroupInviteHandler GroupInvite;
    public event GroupResultHandler GroupInviteResult;
    public event NpcUpdatedHandler NpcUpdated;
    public event PlayerMovedHandler PlayerMoved;
    public event CharacterListHandler CharacterList;
    public event CharacterSelectedHandler CharacterSelected;
    public event CharacterCreatedHandler CharacterCreated;
    public event CharacterDeletedHandler CharacterDeleted;

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
        
#pragma warning disable CS4014
        Task.Run(HandleCommunications);
#pragma warning restore CS4014
    }

    private bool UserCertificateValidationCallback(object sender, X509Certificate x509Certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
        return true;
    }

    public async Task SendChatMessage(string message)
    {
        try
        {
            var packet = CChatMessagePacket.Create(Globals.AccountId, Globals.CharacterId, message, DateTime.UtcNow);

            await _packetSerializer.SerializeToNetwork(stream, packet);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
    
    public async Task SendOpenChatPacket()
    {
        try
        {
            var packet = COpenChatPacket.Create(Globals.AccountId, Globals.CharacterId);

            await _packetSerializer.SerializeToNetwork(stream, packet);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
    
    public async Task SendCloseChatPacket()
    {
        try
        {
            var packet = CCloseChatPacket.Create(Globals.AccountId, Globals.CharacterId);

            await _packetSerializer.SerializeToNetwork(stream, packet);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
    
    private async void HandleCommunications()
    {
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var packet = Serializer.DeserializeWithLengthPrefix<NetworkPacket>(stream, PrefixStyle.Base128);

                switch (packet.Header.Type)
                {
                    case NetworkPacketType.SMSG_PLAYER_POSITION_UPDATE:
                        var movementPacket = _packetDeserializer.Deserialize<SPlayerPositionUpdatePacket>(packet.Header.Type,
                            packet.Payload);
                        try {
                            PlayerMoved?.Invoke(this, movementPacket);
                        } catch (Exception e) {
                            Console.WriteLine(e);
                        }
                        break;
                    case NetworkPacketType.SMSG_NPC_UPDATE:
                        var npcUpdatePacket = Serializer.Deserialize<SNpcUpdatePacket>(new MemoryStream(packet.Payload));
                        try {
                            NpcUpdated?.Invoke(this, npcUpdatePacket);
                        } catch (Exception e) {
                            Console.WriteLine(e);
                        }
                        break;
                    case NetworkPacketType.SMSG_PLAYER_CONNECTED:
                        var connectPacket = Serializer.Deserialize<SPlayerConnectedPacket>(new MemoryStream(packet.Payload));
                        try {
                            PlayerConnected?.Invoke(this, connectPacket);
                        } catch (Exception e) {
                            Console.WriteLine(e);
                        }
                        break;
                    case NetworkPacketType.SMSG_PLAYER_DISCONNECTED:
                        var disconnectPacket = Serializer.Deserialize<SPlayerDisconnectedPacket>(new MemoryStream(packet.Payload));
                        
                        try {
                            PlayerDisconnected?.Invoke(this, disconnectPacket);
                        } catch (Exception e) {
                            Console.WriteLine(e);
                        }
                        break;
                    case NetworkPacketType.SMSG_CHAT_MESSAGE:
                        var messagePacket = Serializer.Deserialize<SChatMessagePacket>(new MemoryStream(packet.Payload));
                        try {
                            ChatMessage?.Invoke(this, messagePacket);
                        } catch (Exception e) {
                            Console.WriteLine(e);
                        }
                        break;
                    case NetworkPacketType.SMSG_AUTH_RESULT:
                        var authResultPacket = Serializer.Deserialize<SAuthResultPacket>(new MemoryStream(packet.Payload));
                        try {
                            AuthResult?.Invoke(this, authResultPacket);
                        } catch (Exception e) {
                            Console.WriteLine(e);
                        }
                        break;
                    case NetworkPacketType.SMSG_GROUP_INVITE:
                        var groupInvitePacket = Serializer.Deserialize<SGroupInvitePacket>(new MemoryStream(packet.Payload));
                        try
                        {
                            GroupInvite?.Invoke(this, groupInvitePacket);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }
                        break;
                    case NetworkPacketType.SMSG_GROUP_INVITE_RESULT:
                        var groupResultPacket = Serializer.Deserialize<SGroupResultPacket>(new MemoryStream(packet.Payload));
                        try
                        {
                            GroupInviteResult?.Invoke(this, groupResultPacket);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }
                        break;
                    case NetworkPacketType.SMSG_CHARACTER_LIST:
                        var characterListPacket = Serializer.Deserialize<SCharacterListPacket>(new MemoryStream(packet.Payload));
                        try
                        {
                            CharacterList?.Invoke(this, characterListPacket);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }
                        break;
                    case NetworkPacketType.SMSG_CHARACTER_SELECTED:
                        var characterSelectedPacket = Serializer.Deserialize<SCharacterSelectedPacket>(new MemoryStream(packet.Payload));
                        try
                        {
                            CharacterSelected?.Invoke(this, characterSelectedPacket);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }
                        break;
                    case NetworkPacketType.SMSG_CHARACTER_CREATED:
                        var characterCreatedPacket = Serializer.Deserialize<SCharacterCreatedPacket>(new MemoryStream(packet.Payload));
                        try
                        {
                            CharacterCreated?.Invoke(this, characterCreatedPacket);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }
                        break;
                    case NetworkPacketType.SMSG_CHARACTER_DELETED:
                        var characterDeletedPacket = Serializer.Deserialize<SCharacterDeletedPacket>(new MemoryStream(packet.Payload));
                        try
                        {
                            CharacterDeleted?.Invoke(this, characterDeletedPacket);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }
                        break;
                    case NetworkPacketType.SMSG_PING:
                        var pingPacket = _packetDeserializer.Deserialize<SPingPacket>(packet.Header.Type,
                            packet.Payload);
                                        
                        var pongPacket = CPongPacket.Create(pingPacket.SequenceNumber, Globals.AccountId, pingPacket.Ticks);
                                        
                        await _packetSerializer.SerializeToNetwork(stream, pongPacket);
                                        
                        break;
                    
                }
            }
        }
        catch (OperationCanceledException e)
        {
            Console.WriteLine(e);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }
    
    public void Disconnect()
    {
        cts.Cancel();
        socket.Disconnect(false);
    }

    public void Dispose()
    {
        cts?.Dispose();
        certificate?.Dispose();
        socket?.Dispose();
        stream?.Dispose();
    }

    public async Task SendCharacterSelectedPacket(int accountId, int characterId)
    {
        var packet = CCharacterSelectedPacket.Create(accountId, characterId);
        
        await _packetSerializer.SerializeToNetwork(stream, packet);
    }

    public async Task SendAuthPacket(string username, string password)
    {
        var packet = CAuthPacket.Create(username, password);
        
        await _packetSerializer.SerializeToNetwork(stream, packet);
    }

    public async Task SendCharacterListPacket(int accountId)
    {
        var packet = CCharacterListPacket.Create(accountId);
        
        await _packetSerializer.SerializeToNetwork(stream, packet);
    }

    public async Task SendCharacterDeletePacket(int accountId, int characterId)
    {
        var packet = CCharacterDeletePacket.Create(accountId, characterId);
        
        await _packetSerializer.SerializeToNetwork(stream, packet);
    }

    public async Task SendCharacterCreatePacket(int accountId, string name, int @class)
    {
        var packet = CCharacterCreatePacket.Create(accountId, name, @class);
        
        await _packetSerializer.SerializeToNetwork(stream, packet);
    }
}
