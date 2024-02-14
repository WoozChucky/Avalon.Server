using System.Reflection;
using Avalon.Network.Packets.Abstractions;

namespace Avalon.Network.Packets.Internal.Deserialization;

public delegate byte[] DecryptFunc(byte[] input);

public interface IPacketDeserializer
{
    T Deserialize<T>(NetworkPacketType packetType, byte[] data, DecryptFunc? decryptFunc = null) where T : class;
    
    Task<T?> DeserializeFromNetwork<T>(Stream source) where T : class;
    
    void RegisterPacketDeserializers(Assembly? assembly = null);
    
    void RegisterCustomPacketDeserializer<T>(NetworkPacketType packetType, Func<byte[], Type, T> deserializer)
        where T : class;

    
}
