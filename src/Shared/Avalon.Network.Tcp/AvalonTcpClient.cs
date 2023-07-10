using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Deserialization;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Map;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Social;
using ProtoBuf;

namespace Avalon.Network.Tcp;

public class AvalonTcpClientSettings
{
    public string Host { get; set; }
    public string CertificatePath { get; set; }
    public int Port { get; set; }
}

public class AvalonTcpClient : IDisposable
{
    private readonly AvalonTcpClientSettings _settings;
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
    public event LogoutHandler Logout;
    public event MapTeleportHandler MapTeleport;

    private readonly CancellationTokenSource _cts;
    private readonly X509Certificate2 _certificate;
    private readonly Socket _socket;
    private SslStream _stream;

    private readonly IPacketDeserializer _packetDeserializer;
    private readonly IPacketSerializer _packetSerializer;

    public int AccountId { get; set; }
    public int CharacterId { get; set; }
    
    public AvalonTcpClient(AvalonTcpClientSettings settings)
    {
        _settings = settings;
        _packetDeserializer = new NetworkPacketDeserializer();
        _packetSerializer = new NetworkPacketSerializer();
        var clientCertBytes = File.ReadAllBytesAsync(_settings.CertificatePath).ConfigureAwait(true).GetAwaiter().GetResult();
        _certificate = new X509Certificate2(clientCertBytes);
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _cts = new CancellationTokenSource();
        _packetDeserializer.RegisterPacketDeserializers();
        _packetSerializer.RegisterPacketSerializers();
    }

    public async Task ConnectAsync()
    {
        await _socket.ConnectAsync(new IPEndPoint(IPAddress.Parse(_settings.Host), _settings.Port)).ConfigureAwait(true);
        _stream = new SslStream(new NetworkStream(_socket), false, UserCertificateValidationCallback);
        await _stream.AuthenticateAsClientAsync(_settings.Host, new X509Certificate2Collection() { _certificate }, SslProtocols.Tls12,
            true).ConfigureAwait(false);
        
#pragma warning disable CS4014
        Task.Run(HandleCommunications);
#pragma warning restore CS4014
    }

    private bool UserCertificateValidationCallback(object sender, X509Certificate x509Certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
        return true;
    }
    
    private async void HandleCommunications()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var packet = Serializer.DeserializeWithLengthPrefix<NetworkPacket>(_stream, PrefixStyle.Base128);

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
                    case NetworkPacketType.SMSG_LOGOUT:
                        var logoutPacket = Serializer.Deserialize<SLogoutPacket>(new MemoryStream(packet.Payload));
                        try
                        {
                            Logout?.Invoke(this, logoutPacket);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }
                        break;
                    case NetworkPacketType.SMSG_MAP_TELEPORT:
                        var teleportPacket = Serializer.Deserialize<SMapTeleportPacket>(new MemoryStream(packet.Payload));
                        try
                        {
                            MapTeleport?.Invoke(this, teleportPacket);
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine(e);
                        }
                        break;
                    case NetworkPacketType.SMSG_PING:
                        var pingPacket = _packetDeserializer.Deserialize<SPingPacket>(packet.Header.Type,
                            packet.Payload);
                                        
                        var pongPacket = CPongPacket.Create(pingPacket.SequenceNumber, AccountId, pingPacket.Ticks);
                                        
                        await _packetSerializer.SerializeToNetwork(_stream, pongPacket);
                                        
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
        _cts.Cancel();
        _socket.Disconnect(false);
    }

    public void Dispose()
    {
        _cts?.Dispose();
        _certificate?.Dispose();
        _socket?.Dispose();
        _stream?.Dispose();
    }

    public async Task SendChatMessage(string message)
    {
        try
        {
            var packet = CChatMessagePacket.Create(AccountId, CharacterId, message, DateTime.UtcNow);

            await _packetSerializer.SerializeToNetwork(_stream, packet);
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
            var packet = COpenChatPacket.Create(AccountId, CharacterId);

            await _packetSerializer.SerializeToNetwork(_stream, packet);
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
            var packet = CCloseChatPacket.Create(AccountId, CharacterId);

            await _packetSerializer.SerializeToNetwork(_stream, packet);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }
    }

    public async Task SendCharacterSelectedPacket(int accountId, int characterId)
    {
        var packet = CCharacterSelectedPacket.Create(accountId, characterId);
        
        await _packetSerializer.SerializeToNetwork(_stream, packet);
    }

    public async Task SendAuthPacket(string username, string password)
    {
        var packet = CAuthPacket.Create(username, password);
        
        await _packetSerializer.SerializeToNetwork(_stream, packet);
    }

    public async Task SendCharacterListPacket(int accountId)
    {
        var packet = CCharacterListPacket.Create(accountId);
        
        await _packetSerializer.SerializeToNetwork(_stream, packet);
    }

    public async Task SendCharacterDeletePacket(int accountId, int characterId)
    {
        var packet = CCharacterDeletePacket.Create(accountId, characterId);
        
        await _packetSerializer.SerializeToNetwork(_stream, packet);
    }

    public async Task SendCharacterCreatePacket(int accountId, string name, int @class)
    {
        var packet = CCharacterCreatePacket.Create(accountId, name, @class);
        
        await _packetSerializer.SerializeToNetwork(_stream, packet);
    }
    
    public async Task SendLogoutPacket(int accountId)
    {
        var packet = CLogoutPacket.Create(accountId);
        
        await _packetSerializer.SerializeToNetwork(_stream, packet);
    }

    public async Task SendCharacterLoadedPacket()
    {
        var packet = CCharacterLoadedPacket.Create(AccountId);
        
        await _packetSerializer.SerializeToNetwork(_stream, packet);
    }

    public async Task SendMapTeleportPacket(int mapId)
    {
        var packet = CMapTeleportPacket.Create(AccountId, CharacterId, mapId);
        
        await _packetSerializer.SerializeToNetwork(_stream, packet);
    }
}
