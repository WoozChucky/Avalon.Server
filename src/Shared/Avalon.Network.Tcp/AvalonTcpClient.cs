using System.Drawing;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Avalon.Common.Threading;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Internal.Deserialization;
using Avalon.Network.Packets.Internal.Exceptions;
using Avalon.Network.Packets.Map;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.Serialization;
using Avalon.Network.Packets.Social;
using Avalon.Network.Packets.World;

namespace Avalon.Network.Tcp;

public class AvalonTcpClientSettings
{
    public string Host { get; set; } = string.Empty;
    public string CertificatePath { get; set; } = string.Empty;
    public int Port { get; set; }
}

public class AvalonTcpClient : IDisposable
{
    private readonly AvalonTcpClientSettings _settings;
    public event PlayerConnectedHandler? PlayerConnected;
    public event PlayerDisconnectedHandler? PlayerDisconnected;
    public event ChatMessageHandler? ChatMessage;
    public event AuthResultHandler? AuthResult;
    public event GroupInviteHandler? GroupInvite;
    public event GroupResultHandler? GroupInviteResult;
    public event NpcUpdatedHandler? NpcUpdated;
    public event PlayerMovedHandler? PlayerMoved;
    public event CharacterListHandler? CharacterList;
    public event CharacterSelectedHandler? CharacterSelected;
    public event CharacterCreatedHandler? CharacterCreated;
    public event CharacterDeletedHandler? CharacterDeleted;
    public event LogoutHandler? Logout;
    public event MapTeleportHandler? MapTeleport;

    private readonly CancellationTokenSource _cts;
    private readonly X509Certificate2 _certificate;
    private readonly IPAddress _ipAddress;
    private readonly Socket _socket;
    private readonly RingBuffer<Packet> _receivedPacketBuffer;
    private readonly RingBuffer<NetworkPacket> _sendPacketBuffer;
    private readonly AvalonCryptography _cryptography;
    
    private SslStream _stream = null!;

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
        _cryptography = new AvalonCryptography();

        _receivedPacketBuffer = new RingBuffer<Packet>(100);
        _sendPacketBuffer = new RingBuffer<NetworkPacket>(100);

        _ipAddress =
            Dns.GetHostAddresses(_settings.Host).ToList()
                .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork) ??
            throw new Exception("Unable to resolve host: " + _settings.Host + "");
        
        _packetDeserializer.RegisterPacketDeserializers();
        _packetSerializer.RegisterPacketSerializers();
    }

    public async Task ConnectAsync()
    {
        await _socket.ConnectAsync(new IPEndPoint(_ipAddress, _settings.Port)).ConfigureAwait(true);
        _stream = new SslStream(new NetworkStream(_socket), false, UserCertificateValidationCallback);
        await _stream.AuthenticateAsClientAsync(_settings.Host, new X509Certificate2Collection() { _certificate }, SslProtocols.Tls12,
            true).ConfigureAwait(false);
        
#pragma warning disable CS4014
        Task.Run(ProcessPacketsAsync);
        Task.Run(HandleCommunications);
        Task.Run(ProcessReceivedPackets);
#pragma warning restore CS4014
    }

    private bool UserCertificateValidationCallback(object sender, X509Certificate x509Certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
        return true;
    }

    private async void ProcessReceivedPackets()
    {
        try
        {
            async void Send(Action action)
            {
                await Task.Run(() =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                });
            }
            
            while (!_cts.IsCancellationRequested)
            {
                var packet = await _receivedPacketBuffer.DequeueAsync(_cts.Token);

                if (packet is null)
                {
                    Console.WriteLine("Received packet in packet processor thread");
                    continue;
                }

                switch (packet)
                {
                    case SPlayerPositionUpdatePacket p:
                        Send(() => PlayerMoved?.Invoke(this, p));
                        break;
                    case SNpcUpdatePacket p:
                        Send(() => NpcUpdated?.Invoke(this, p));
                        break;
                    case SPlayerConnectedPacket p:
                        Send(() => PlayerConnected?.Invoke(this, p));
                        break;
                    case SPlayerDisconnectedPacket p:
                        Send(() => PlayerDisconnected?.Invoke(this, p));
                        break;
                    case SChatMessagePacket p:
                        Send(() => ChatMessage?.Invoke(this, p));
                        break;
                    case SAuthResultPacket p:
                        Send(() => AuthResult?.Invoke(this, p));
                        break;
                    case SGroupInvitePacket p:
                        Send(() => GroupInvite?.Invoke(this, p));
                        break;
                    case SGroupResultPacket p:
                        Send(() => GroupInviteResult?.Invoke(this, p));
                        break;
                    case SCharacterListPacket p:
                        Send(() => CharacterList?.Invoke(this, p));
                        break;
                    case SCharacterSelectedPacket p:
                        Send(() => CharacterSelected?.Invoke(this, p));
                        break;
                    case SCharacterCreatedPacket p:
                        Send(() => CharacterCreated?.Invoke(this, p));
                        break;
                    case SCharacterDeletedPacket p:
                        Send(() => CharacterDeleted?.Invoke(this, p));
                        break;
                    case SLogoutPacket p:
                        Send(() => Logout?.Invoke(this, p));
                        break;
                    case SMapTeleportPacket p:
                        Send(() => MapTeleport?.Invoke(this, p));
                        break;
                    case SPingPacket p:
                        Send(() =>
                        {
                            var pongPacket = CPongPacket.Create(p.SequenceNumber, AccountId, p.Ticks);
                            SendPacket(pongPacket);
                        });
                        break;
                    
                }
                
            }
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
    
    private async void HandleCommunications()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var packet = await _packetDeserializer.DeserializeFromNetwork<NetworkPacket>(_stream);
                
                if (packet == null)
                {
                    Console.WriteLine("Received null packet in network thread");
                    continue;
                }
                
                var innerPacket = GetInnerPacket(packet);
                
                _receivedPacketBuffer.Enqueue(innerPacket);
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
    
    private Packet GetInnerPacket(NetworkPacket packet)
    {
        return packet.Header.Type switch
        {
            // Auth
            NetworkPacketType.SMSG_AUTH_RESULT => _packetDeserializer.Deserialize<SAuthResultPacket>(packet.Header.Type, packet.Payload),
            NetworkPacketType.SMSG_LOGOUT => _packetDeserializer.Deserialize<SLogoutPacket>(packet.Header.Type, packet.Payload),
            
            // Account
            NetworkPacketType.SMSG_PLAYER_CONNECTED => _packetDeserializer.Deserialize<SPlayerConnectedPacket>(packet.Header.Type, packet.Payload),
            NetworkPacketType.SMSG_PLAYER_DISCONNECTED => _packetDeserializer.Deserialize<SPlayerDisconnectedPacket>(packet.Header.Type, packet.Payload),
            NetworkPacketType.SMSG_PLAYER_POSITION_UPDATE => _packetDeserializer.Deserialize<SPlayerPositionUpdatePacket>(packet.Header.Type, packet.Payload),
            
            // TODO: Refactor this packet type and packet name
            NetworkPacketType.SMSG_NPC_UPDATE => _packetDeserializer.Deserialize<SNpcUpdatePacket>(packet.Header.Type, packet.Payload),

            // Character
            NetworkPacketType.SMSG_CHARACTER_LIST => _packetDeserializer.Deserialize<SCharacterListPacket>(packet.Header.Type, packet.Payload, _cryptography.Decrypt),
            NetworkPacketType.SMSG_CHARACTER_SELECTED => _packetDeserializer.Deserialize<SCharacterSelectedPacket>(packet.Header.Type, packet.Payload),
            NetworkPacketType.SMSG_CHARACTER_CREATED => _packetDeserializer.Deserialize<SCharacterCreatedPacket>(packet.Header.Type, packet.Payload),
            NetworkPacketType.SMSG_CHARACTER_DELETED => _packetDeserializer.Deserialize<SCharacterDeletedPacket>(packet.Header.Type, packet.Payload),
            
            // Map
            NetworkPacketType.SMSG_MAP_TELEPORT => _packetDeserializer.Deserialize<SMapTeleportPacket>(packet.Header.Type, packet.Payload),
            
            // Social
            NetworkPacketType.SMSG_CHAT_MESSAGE => _packetDeserializer.Deserialize<SChatMessagePacket>(packet.Header.Type, packet.Payload),
            NetworkPacketType.SMSG_CHAT_OPEN => _packetDeserializer.Deserialize<SOpenChatPacket>(packet.Header.Type, packet.Payload),
            NetworkPacketType.SMSG_CHAT_CLOSE => _packetDeserializer.Deserialize<SCloseChatPacket>(packet.Header.Type, packet.Payload),
            NetworkPacketType.SMSG_GROUP_INVITE => _packetDeserializer.Deserialize<SGroupInvitePacket>(packet.Header.Type, packet.Payload),
            NetworkPacketType.SMSG_GROUP_INVITE_RESULT => _packetDeserializer.Deserialize<SGroupResultPacket>(packet.Header.Type, packet.Payload), 
            
            // Generic
            NetworkPacketType.SMSG_PING => _packetDeserializer.Deserialize<SPingPacket>(packet.Header.Type, packet.Payload),
            
            _ => throw new PacketHandlerException("Unknown packet type " + packet.Header.Type)
        };
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
        var packet = CChatMessagePacket.Create(AccountId, CharacterId, message, DateTime.UtcNow);

        await SendPacket(packet);
    }
    
    public async Task SendOpenChatPacket()
    {
        var packet = COpenChatPacket.Create(AccountId, CharacterId);

        await SendPacket(packet);
    }
    
    public async Task SendCloseChatPacket()
    {
        var packet = CCloseChatPacket.Create(AccountId, CharacterId);

        await SendPacket(packet);
    }

    public async Task SendCharacterSelectedPacket(int accountId, int characterId)
    {
        var packet = CCharacterSelectedPacket.Create(accountId, characterId);
        
        await SendPacket(packet);
    }

    public async Task SendAuthPacket(string username, string password)
    {
        var packet = CAuthPacket.Create(username, password);
        
        await SendPacket(packet);
    }

    public async Task SendCharacterListPacket(int accountId)
    {
        var packet = CCharacterListPacket.Create(accountId, _cryptography.Encrypt);
        
        await SendPacket(packet);
    }

    public async Task SendCharacterDeletePacket(int accountId, int characterId)
    {
        var packet = CCharacterDeletePacket.Create(accountId, characterId);
        
        await SendPacket(packet);
    }

    public async Task SendCharacterCreatePacket(int accountId, string name, int @class)
    {
        var packet = CCharacterCreatePacket.Create(accountId, name, @class);
        
        await SendPacket(packet);
    }
    
    public async Task SendLogoutPacket(int accountId)
    {
        var packet = CLogoutPacket.Create(accountId);
        
        await SendPacket(packet);
    }

    public async Task SendCharacterLoadedPacket()
    {
        var packet = CCharacterLoadedPacket.Create(AccountId);
        
        await SendPacket(packet);
    }

    public async Task SendMapTeleportPacket(int mapId)
    {
        var packet = CMapTeleportPacket.Create(AccountId, CharacterId, mapId);
        
        await SendPacket(packet);
    }
    
    public async Task SendInteractPacket(Rectangle targetArea)
    {
        var packet = CInteractPacket.Create(AccountId, CharacterId, targetArea.X, targetArea.Y, targetArea.Width, targetArea.Height);
        
        await SendPacket(packet);
    }
    
    private async Task SendPacket(NetworkPacket packet)
    {
        _sendPacketBuffer.Enqueue(packet);
    }
    
    private async Task ProcessPacketsAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var packet = await _sendPacketBuffer.DequeueAsync(_cts.Token);

                if (packet is null)
                {
                    continue;
                }
                
                await SendQueuedPacketAsync(packet);
            }
        }
        catch (OperationCanceledException)
        {
            
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
    }
    
    private async Task SendQueuedPacketAsync(NetworkPacket packet)
    {
        await _packetSerializer.SerializeToNetwork(_stream, packet);
    }
    
    public void InitializeCryptography(byte[] sessionKey)
    {
        _cryptography.Initialize(sessionKey);
    }
    
    public byte[] Encrypt(byte[] data)
    {
        return _cryptography.Encrypt(data);
    }
    
    public byte[] Decrypt(byte[] data)
    {
        return _cryptography.Decrypt(data);
    }
    
    private class AvalonCryptography
    {
        private Aes _aes;

        public void Initialize(byte[] sessionKey)
        {
            _aes = Aes.Create();
            _aes.Key = sessionKey;
            _aes.IV = new byte[] {0x5A, 0x36, 0x7F, 0x8D, 0xE9, 0x02, 0xC4, 0xAF, 0x71, 0x5E, 0x9B, 0x44, 0xD7, 0x1A, 0x80, 0x3F};
        }

        public byte[] Encrypt(byte[] data)
        {
            using var memoryStream = new MemoryStream();

            using (var encryptor = _aes.CreateEncryptor())
            {
                using (var csEncrypt = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write))
                {
                    csEncrypt.Write(data, 0, data.Length);
                    csEncrypt.FlushFinalBlock();
                }
            }

            return memoryStream.ToArray();
        }

        public byte[] Decrypt(byte[] data)
        {
            using var memoryStream = new MemoryStream();

            using (var decryptor = _aes.CreateDecryptor())
            {
                using (var csDecrypt = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Write))
                {
                    csDecrypt.Write(data, 0, data.Length);
                    csDecrypt.FlushFinalBlock();
                }
            }

            return memoryStream.ToArray();
        }
    }
}
