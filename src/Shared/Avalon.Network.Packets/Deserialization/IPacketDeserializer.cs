using System.Reflection;
using Avalon.Network.Packets.Abstractions;

namespace Avalon.Network.Packets.Deserialization;

public interface IPacketDeserializer
{
    T Deserialize<T>(NetworkPacketType packetType, byte[] data) where T : class;
    
    Task<T?> DeserializeFromNetwork<T>(Stream source) where T : class;
    
    void RegisterPacketDeserializers(Assembly? assembly = null);
    
    void RegisterCustomPacketDeserializer<T>(NetworkPacketType packetType, Func<byte[], Type, T> deserializer)
        where T : class;

    
}
