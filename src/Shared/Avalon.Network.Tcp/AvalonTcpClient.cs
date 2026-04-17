// LEGACY — This client implementation is retired and not wired into any production code.
// It may be outdated and should not be used as a reference for the current protocol.

using System.Drawing;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Avalon.Common.Cryptography;
using Avalon.Common.Threading;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Character;
using Avalon.Network.Packets.Generic;
using Avalon.Network.Packets.Handshake;
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

[Obsolete("Retired legacy client. Not wired into production code and may not reflect the current protocol.")]
public class AvalonTcpClient : IDisposable
{
    private readonly AvalonTcpClientSettings _settings;
    public event AccountRegisterHandler? RegisterResult;
    public event PlayerConnectedHandler? PlayerConnected;
    public event PlayerDisconnectedHandler? PlayerDisconnected;
    public event ChatMessageHandler? ChatMessage;
    public event AuthResultHandler? AuthResult;
    public event GroupInviteHandler? GroupInvite;
    public event GroupResultHandler? GroupInviteResult;
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
    private readonly IAvalonCryptoSession _cryptography;

    private SslStream _stream = null!;

    private readonly IPacketDeserializer _packetDeserializer;
    private readonly IPacketSerializer _packetSerializer;

    private long _bytesReceived;

    private long _packetsReceived;

    public ulong AccountId { get; set; }
    public ulong CharacterId { get; set; }

    public AvalonTcpClient(AvalonTcpClientSettings settings)
    {
        _settings = settings;
        _packetDeserializer = new NetworkPacketDeserializer();
        _packetSerializer = new NetworkPacketSerializer();
        var clientCertBytes = File.ReadAllBytesAsync(_settings.CertificatePath).ConfigureAwait(true).GetAwaiter().GetResult();
        _certificate = new X509Certificate2(clientCertBytes);
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        _socket.NoDelay = true;
        _cts = new CancellationTokenSource();
        _cryptography = new AvalonCryptoSession();

        _receivedPacketBuffer = new RingBuffer<Packet>("RECV", 100);
        _sendPacketBuffer = new RingBuffer<NetworkPacket>("SND", 100);

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
        Task.Run(UpdateMetricsAsync);
#pragma warning restore CS4014

        await RequestServerInfoPacket();
    }

    private bool UserCertificateValidationCallback(object sender, X509Certificate x509Certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
    {
        return true;
    }

    private async Task UpdateMetricsAsync()
    {
        long previousBytesReceived = 0;
        long previousPacketsReceived = 0;
        var lastUpdate = DateTime.UtcNow;

        while (!_cts.IsCancellationRequested)
        {
            await Task.Delay(5000, _cts.Token).ConfigureAwait(false);

            if (true)
            {
                var currentBytesReceived = Interlocked.Read(ref _bytesReceived);
                var currentPacketsReceived = Interlocked.Read(ref _packetsReceived);

                // Bytes conversion to Mb
                var kbBytesReceived = currentBytesReceived / 1024;
                var mgBytesReceived = currentBytesReceived / (1024 * 1024);
                var gbBytesReceived = currentBytesReceived / (1024 * 1024 * 1024);

                // Byte rate calculation
                var bytesInLastSecond = currentBytesReceived - previousBytesReceived;
                var byteRate = Math.Round(bytesInLastSecond / Math.Max((DateTime.UtcNow - lastUpdate).TotalSeconds, 1), 2);

                // Packet rate calculation
                var packetsInLastSecond = currentPacketsReceived - previousPacketsReceived;
                var packetRate = (int)(packetsInLastSecond / Math.Max((DateTime.UtcNow - lastUpdate).TotalSeconds, 1));


                Console.WriteLine($"Bytes received: {kbBytesReceived}Kb, Rate: {byteRate} bytes/second");
                Console.WriteLine($"Packets received: {currentPacketsReceived}, Rate: {packetRate} packets/second");
                Console.WriteLine("---------------------------------");

                previousPacketsReceived = currentPacketsReceived;
                previousBytesReceived = currentBytesReceived;
                lastUpdate = DateTime.UtcNow;
            }
        }
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
                    case SServerInfoPacket p:
                        _cryptography.Initialize(p.PublicKey);
                        await SendPacket(CClientInfoPacket.Create(_cryptography.GetPublicKey()));
                        break;
                    case SHandshakePacket p:
                        var data = p.HandshakeData;
                        var result = CHandshakePacket.Create(data, _cryptography.Encrypt);
                        await SendPacket(result);
                        break;
                    case SHandshakeResultPacket p:
                        if (!p.Verified)
                        {
                            throw new Exception("Handshake failed");
                        }
                        break;
                    case SRegisterResultPacket p:
                        Send(() => RegisterResult?.Invoke(this, p));
                        break;
                    case SPlayerPositionUpdatePacket p:
                        Send(() => PlayerMoved?.Invoke(this, p));
                        break;
                    case SPlayerConnectedPacket p:
                        Console.WriteLine("Received player connected packet");
                        Send(() => PlayerConnected?.Invoke(this, p));
                        break;
                    case SPlayerDisconnectedPacket p:
                        Console.WriteLine("Received player disconnected packet");
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
                            //var pongPacket = CPongPacket.Create(p.SequenceNumber, p.ServerTimestamp);
                            //SendPacket(pongPacket);
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

                Interlocked.Add(ref _bytesReceived, packet.Size);
                Interlocked.Increment(ref _packetsReceived);

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
        DecryptFunc? decryptFunc = null;
        if (packet.Header.Flags == NetworkPacketFlags.Encrypted)
            decryptFunc = data =>
            {
                byte[] output = new byte[data.Length];
                int len = _cryptography!.Decrypt(data, output);
                return output[..len];
            };

        if (packet.Header.Flags == NetworkPacketFlags.Encrypted && decryptFunc == null)
            throw new PacketHandlerException("Encrypted packet received without session key");

        return packet.Header.Type switch
        {
            // Handshake
            NetworkPacketType.SMSG_SERVER_INFO => _packetDeserializer.Deserialize<SServerInfoPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.SMSG_SERVER_HANDSHAKE => _packetDeserializer.Deserialize<SHandshakePacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.SMSG_SERVER_HANDSHAKE_RESULT => _packetDeserializer.Deserialize<SHandshakeResultPacket>(packet.Header.Type, packet.Payload, decryptFunc),


            // Auth
            NetworkPacketType.SMSG_AUTH_RESULT => _packetDeserializer.Deserialize<SAuthResultPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.SMSG_LOGOUT => _packetDeserializer.Deserialize<SLogoutPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.SMSG_REGISTER_RESULT => _packetDeserializer.Deserialize<SRegisterResultPacket>(packet.Header.Type, packet.Payload, decryptFunc),

            // Account
            NetworkPacketType.SMSG_CHARACTER_CONNECTED => _packetDeserializer.Deserialize<SPlayerConnectedPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.SMSG_CHARACTER_DISCONNECTED => _packetDeserializer.Deserialize<SPlayerDisconnectedPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.SMSG_PLAYER_POSITION_UPDATE => _packetDeserializer.Deserialize<SPlayerPositionUpdatePacket>(packet.Header.Type, packet.Payload, decryptFunc),

            // Character
            NetworkPacketType.SMSG_CHARACTER_LIST => _packetDeserializer.Deserialize<SCharacterListPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.SMSG_CHARACTER_SELECTED => _packetDeserializer.Deserialize<SCharacterSelectedPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.SMSG_CHARACTER_CREATED => _packetDeserializer.Deserialize<SCharacterCreatedPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.SMSG_CHARACTER_DELETED => _packetDeserializer.Deserialize<SCharacterDeletedPacket>(packet.Header.Type, packet.Payload, decryptFunc),

            // Map
            NetworkPacketType.SMSG_MAP_TELEPORT => _packetDeserializer.Deserialize<SMapTeleportPacket>(packet.Header.Type, packet.Payload, decryptFunc),

            // Social
            NetworkPacketType.SMSG_CHAT_MESSAGE => _packetDeserializer.Deserialize<SChatMessagePacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.SMSG_CHAT_OPEN => _packetDeserializer.Deserialize<SOpenChatPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.SMSG_CHAT_CLOSE => _packetDeserializer.Deserialize<SCloseChatPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.SMSG_GROUP_INVITE => _packetDeserializer.Deserialize<SGroupInvitePacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.SMSG_GROUP_INVITE_RESULT => _packetDeserializer.Deserialize<SGroupResultPacket>(packet.Header.Type, packet.Payload, decryptFunc),

            // Generic
            NetworkPacketType.SMSG_PING => _packetDeserializer.Deserialize<SPingPacket>(packet.Header.Type, packet.Payload, decryptFunc),
            NetworkPacketType.SMSG_DISCONNECT => _packetDeserializer.Deserialize<SDisconnectPacket>(packet.Header.Type, packet.Payload, decryptFunc),

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
        var packet = CChatMessagePacket.Create(message, DateTime.UtcNow, _cryptography.Encrypt);

        await SendPacket(packet);
    }

    public async Task SendOpenChatPacket()
    {
        var packet = COpenChatPacket.Create(AccountId, CharacterId, _cryptography.Encrypt);

        await SendPacket(packet);
    }

    public async Task SendCloseChatPacket()
    {
        var packet = CCloseChatPacket.Create(AccountId, CharacterId, _cryptography.Encrypt);

        await SendPacket(packet);
    }

    public async Task SendCharacterSelectedPacket(uint characterId)
    {
        var packet = CCharacterSelectedPacket.Create(characterId, _cryptography.Encrypt);

        await SendPacket(packet);
    }

    public async Task SendAuthPacket(string username, string password)
    {
        var packet = CAuthPacket.Create(username, password, _cryptography.Encrypt);

        await SendPacket(packet);
    }

    public async Task SendCharacterListPacket()
    {
        var packet = CCharacterListPacket.Create(_cryptography.Encrypt);

        await SendPacket(packet);
    }

    public async Task SendCharacterDeletePacket(uint characterId)
    {
        var packet = CCharacterDeletePacket.Create(characterId, _cryptography.Encrypt);

        await SendPacket(packet);
    }

    public async Task SendCharacterCreatePacket(string name, int @class)
    {
        var packet = CCharacterCreatePacket.Create(name, @class, _cryptography.Encrypt);

        await SendPacket(packet);
    }

    public async Task SendLogoutPacket(ulong accountId)
    {
        var packet = CLogoutPacket.Create(accountId, _cryptography.Encrypt);

        await SendPacket(packet);
    }

    public async Task SendCharacterLoadedPacket()
    {
        var packet = CCharacterLoadedPacket.Create(_cryptography.Encrypt);

        await SendPacket(packet);
    }

    public async Task SendMapTeleportPacket(int mapId)
    {
        var packet = CMapTeleportPacket.Create(AccountId, CharacterId, mapId, _cryptography.Encrypt);

        await SendPacket(packet);
    }

    public async Task SendInteractPacket(Rectangle targetArea)
    {
        var packet = CInteractPacket.Create(AccountId, CharacterId, targetArea.X, targetArea.Y, targetArea.Width, targetArea.Height, _cryptography.Encrypt);

        await SendPacket(packet);
    }

    public async Task RequestServerInfoPacket()
    {
        var packet = CRequestServerInfoPacket.Create("TODO");

        await SendPacket(packet);
    }

    private Task SendPacket(NetworkPacket packet)
    {
        _sendPacketBuffer.Enqueue(packet);
        return Task.CompletedTask;
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

    public byte[] PublicKey()
    {
        return _cryptography.GetPublicKey();
    }

    public async Task BroadcastMovementUpdates(double time, float x, float y, float z, float velX, float velY, float velZ, float rotation)
    {
        if (velX == 0 && velY == 0)
        {
            Console.WriteLine("No movement detected, skipping...");
        }

        await SendPacket(CPlayerMovementPacket.Create(time, x, y, z, velX, velY, velZ, rotation, _cryptography.Encrypt));
    }

    public async Task SendRegisterPacket(string username, string email, string password)
    {
        var packet = CRegisterPacket.Create(username, email, password, _cryptography.Encrypt);

        await SendPacket(packet);
    }
}
