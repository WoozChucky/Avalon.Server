using System.Collections.Concurrent;
using Avalon.Network.Packets.Crypto;
using ProtoBuf;

namespace Avalon.Network.Packets;

public class PacketGenerator
{
    
    private readonly ConcurrentDictionary<NetworkPacketType, Func<object>> _packetFactories;

    public PacketGenerator()
    {
        // Base Packet Serializers
        Serializer.PrepareSerializer<NetworkPacket>();
        Serializer.PrepareSerializer<NetworkPacketHeader>();
        
        // Server Packet Serializers
        Serializer.PrepareSerializer<SCryptoKeyPacket>();
        
        // Client Packet Serializers
        Serializer.PrepareSerializer<CRequestCryptoKeyPacket>();
    }
}
