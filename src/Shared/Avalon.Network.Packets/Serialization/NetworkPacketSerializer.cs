using System.Reflection;
using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Serialization;

public class NetworkPacketSerializer : IPacketSerializer
{
    public NetworkPacketSerializer()
    {
        // Base Packet Serializers
        Serializer.PrepareSerializer<NetworkPacket>();
        Serializer.PrepareSerializer<NetworkPacketHeader>();
    }
    
    public Task SerializeToNetwork<T>(Stream destination, T packet) where T : class
    {
        Serializer.SerializeWithLengthPrefix(destination, packet, PrefixStyle.Base128);
        
        return Task.CompletedTask;
    }
    
    public Task Serialize<T>(Stream destination, T packet) where T : class
    {
        Serializer.Serialize(destination, packet);
        return Task.CompletedTask;
    }

    public void RegisterPacketSerializers(Assembly? assembly = null)
    {
        var packetTypes = assembly == null
            ? GetNetworkPacketTypes(typeof(NetworkPacketSerializer).Assembly)
            : GetNetworkPacketTypes(assembly);
        
        var serializerType = typeof(Serializer);
        var genericPrepareSerializerMethod = serializerType.GetMethods()
            .Single(m => m is { Name: "PrepareSerializer", IsGenericMethod: true } && m.GetParameters().Length == 0);
        
        foreach (var packetType in packetTypes)
        {
            var closedPrepareSerializerMethod = genericPrepareSerializerMethod.MakeGenericMethod(packetType);
            closedPrepareSerializerMethod.Invoke(null, null);
        }
    }

    private IEnumerable<Type> GetNetworkPacketTypes(Assembly assembly)
    {
        return assembly.GetTypes()
            .Where(type =>
                type.IsClass &&
                type.GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Any(field => field.FieldType == typeof(NetworkPacketType))
            );
    }
}
