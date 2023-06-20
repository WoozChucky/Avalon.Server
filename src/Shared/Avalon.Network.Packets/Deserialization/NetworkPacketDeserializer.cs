using System.Collections.Concurrent;
using System.Reflection;
using Avalon.Network.Packets.Exceptions;
using ProtoBuf;

namespace Avalon.Network.Packets.Deserialization;

public class NetworkPacketDeserializer : IPacketDeserializer
{
    private const string PacketTypeFieldName = "PacketType";
    
    private readonly ConcurrentDictionary<NetworkPacketType, Func<byte[], Type, object>> _packetDeserializerFactories;
    private readonly ConcurrentDictionary<Type, MethodInfo> _packetDeserializerMethods;

    public NetworkPacketDeserializer()
    {
        _packetDeserializerFactories = new ConcurrentDictionary<NetworkPacketType, Func<byte[], Type, object>>();
        _packetDeserializerMethods = new ConcurrentDictionary<Type, MethodInfo>();
    }
    
    public T Deserialize<T>(NetworkPacketType packetType, byte[] data) where T : class
    {
        if (!_packetDeserializerFactories.ContainsKey(packetType))
            throw new PacketDeserializationException($"Packet deserializer not registered for type ({packetType}).");
        
        return _packetDeserializerFactories[packetType](data, typeof(T)) as T ?? throw new PacketDeserializationException("Packet deserialization failed.");
    }

    public Task<T> DeserializeFromNetwork<T>(Stream source) where T : class
    {
        return Task.FromResult(Serializer.DeserializeWithLengthPrefix<T>(source, PrefixStyle.Base128));
    }

    public void RegisterCustomPacketDeserializer<T>(NetworkPacketType packetType, Func<byte[], Type, T> deserializer) where T : class
    {
        _packetDeserializerFactories.TryAdd(packetType, deserializer);
    }
    
    public void RegisterPacketDeserializers(Assembly? assembly = null)
    {
        var packetTypes = assembly == null
            ? GetNetworkPacketTypes(typeof(NetworkPacketDeserializer).Assembly)
            : GetNetworkPacketTypes(assembly);
        
        var serializerType = typeof(Serializer);

        var genericDeserializeMethod = serializerType.GetMethods()
            .Single(m => m is { Name: "Deserialize", IsGenericMethod: true } && m.GetParameters().Length == 1);

        foreach (var packetType in packetTypes)
        {
            var packetTypeValue = packetType.GetField(PacketTypeFieldName, BindingFlags.Public | BindingFlags.Static)
                ?.GetValue(null);

            if (packetTypeValue is NetworkPacketType pType)
            {
                var closedDeserializeMethod = genericDeserializeMethod.MakeGenericMethod(packetType);

                _packetDeserializerMethods.TryAdd(packetType, closedDeserializeMethod);
                _packetDeserializerFactories.TryAdd(pType, InternalDeserialization);
            }
        }
    }

    private object InternalDeserialization(byte[] arg, Type type)
    {
        using var ms = new MemoryStream(arg);
        
        return _packetDeserializerMethods[type].Invoke(null, new object?[]{ms}) 
               ?? throw new PacketDeserializationException("Packet deserialization failed.");
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
