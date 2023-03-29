using System.Collections.Concurrent;
using System.Reflection;
using Avalon.Network.Packets.Crypto;
using ProtoBuf;

namespace Avalon.Network.Packets;

public class PacketGenerator
{
    
    private const string PacketTypeFieldName = "PacketType";
    
    private readonly ConcurrentDictionary<NetworkPacketType, Func<byte[], object>> _packetDeserializerFactories;

    public PacketGenerator()
    {
        _packetDeserializerFactories = new ConcurrentDictionary<NetworkPacketType, Func<byte[], object>>();
        
        // Base Packet Serializers
        Serializer.PrepareSerializer<NetworkPacket>();
        Serializer.PrepareSerializer<NetworkPacketHeader>();
    }
    
    public void RegisterPacketDeserializers()
    {
        var packetTypes = typeof(PacketGenerator).Assembly.GetTypes()
            .Where(type =>
                type.IsClass &&
                type.GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Any(field => field.FieldType == typeof(NetworkPacketType))
            );
        
        var serializerType = typeof(Serializer);
        var genericPrepareSerializerMethod = serializerType.GetMethods()
            .Single(m => m is { Name: "PrepareSerializer", IsGenericMethod: true } && m.GetParameters().Length == 0);

        foreach (var packetType in packetTypes)
        {
            var closedPrepareSerializerMethod = genericPrepareSerializerMethod.MakeGenericMethod(packetType);
            closedPrepareSerializerMethod.Invoke(null, null);
            
            var packetTypeValue = packetType.GetField(PacketTypeFieldName, BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null);

            if (packetTypeValue is NetworkPacketType pType)
            {
                _packetDeserializerFactories.TryAdd(pType, InternalDeserialization);
            }
        }
    }
    
    public void RegisterCustomPacketDeserializer<T>(NetworkPacketType packetType, Func<byte[], T> deserializer) where T : class
    {
        Serializer.PrepareSerializer<T>();
        _packetDeserializerFactories.TryAdd(packetType, deserializer);
    }

    private object InternalDeserialization(byte[] arg)
    {
        return Serializer.Deserialize<object>(new MemoryStream(arg));
    }

    public T Deserialize<T>(NetworkPacketType packetType, byte[] data) where T : class
    {
        return _packetDeserializerFactories[packetType](data) as T ?? throw new InvalidOperationException("Packet deserialization failed.");
    }
}
