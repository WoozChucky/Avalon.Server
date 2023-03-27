using System.Collections.Concurrent;
using Avalon.Network.Packets.Crypto;
using ProtoBuf;

namespace Avalon.Network.Packets;

public class PacketGenerator
{
    
    private readonly ConcurrentDictionary<NetworkPacketType, Func<object, byte[]>> _packetFactories;

    public PacketGenerator()
    {
        _packetFactories = new ConcurrentDictionary<NetworkPacketType, Func<object, byte[]>>();
        
        // Base Packet Serializers
        Serializer.PrepareSerializer<NetworkPacket>();
        Serializer.PrepareSerializer<NetworkPacketHeader>();
        
        // Server Packet Serializers
        Serializer.PrepareSerializer<SCryptoKeyPacket>();
        
        // Client Packet Serializers
        Serializer.PrepareSerializer<CRequestCryptoKeyPacket>();
    }
    
    public void RegisterPackets()
    {
        
        _packetFactories.TryAdd(NetworkPacketType.CMSG_REQUEST_ENCRYPTION_KEY, SerializeCRequestCryptoKeyPacket);
        _packetFactories.TryAdd(NetworkPacketType.SMSG_ENCRYPTION_KEY, SerializeSCryptoKeyPacket);
    }
    
    public object Deserialize(NetworkPacketType packetType, byte[] data)
    {
        return _packetFactories[packetType](data);
    }
}
