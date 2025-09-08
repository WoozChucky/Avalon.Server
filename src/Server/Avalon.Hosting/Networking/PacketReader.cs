using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Microsoft.Extensions.Logging;
using ProtoBuf;

namespace Avalon.Hosting.Networking;

public delegate byte[] DecryptFunc(byte[] data);

public interface IPacketReader
{
    IAsyncEnumerable<NetworkPacket> EnumerateAsync(PacketStream stream, CancellationToken token = default);
    Packet? Read(NetworkPacket packet);
    void Decrypt(NetworkPacket packet, DecryptFunc decryptFunc);
}

public class PacketReader : IPacketReader
{
    private readonly int _bufferSize;
    private readonly ILogger<PacketReader> _logger;

    private readonly Dictionary<NetworkPacketType, (Type packet, MethodInfo deserialize)> _packetTypes = new();

    public PacketReader(ILoggerFactory loggerFactory, Type[] packetTypes)
    {
        _logger = loggerFactory.CreateLogger<PacketReader>();
        _bufferSize = 4096; //TODO: Get from configuration

        MethodInfo genericDeserializeMethod = typeof(Serializer).GetMethods()
            .Single(m =>
                m is {Name: "Deserialize", IsGenericMethod: true} && m.GetParameters().Length == 3 &&
                m.GetParameters()[0].ParameterType == typeof(ReadOnlyMemory<byte>));

        foreach (Type packetType in packetTypes)
        {
            FieldInfo? networkPacketTypeInfo = packetType.GetFields(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(field => field.FieldType == typeof(NetworkPacketType));
            if (networkPacketTypeInfo == null)
            {
                _logger.LogWarning("Packet type {PacketType} does not have a NetworkPacketType field", packetType);
            }

            NetworkPacketType networkPacketType = (NetworkPacketType)networkPacketTypeInfo!.GetValue(null)!;

            MethodInfo closedDeserializeMethod = genericDeserializeMethod.MakeGenericMethod(packetType);
            _packetTypes.Add(networkPacketType, (packetType, closedDeserializeMethod));
        }
    }

    public async IAsyncEnumerable<NetworkPacket> EnumerateAsync(PacketStream stream,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        // Delegate reading logic to PacketStream to centralize buffer/span handling
        await foreach (var packet in stream.EnumerateAsync(_bufferSize, token))
        {
            yield return packet;
        }
    }

    public void Decrypt(NetworkPacket packet, DecryptFunc decryptFunc) => packet.Payload = decryptFunc(packet.Payload);

    public Packet? Read(NetworkPacket packet)
    {
        if (!_packetTypes.TryGetValue(packet.Header.Type, out (Type packet, MethodInfo deserialize) p))
        {
            _logger.LogWarning("Unknown packet type {PacketType}", packet.Header.Type);
            return null;
        }

        ReadOnlyMemory<byte> payloadMemory = new(packet.Payload);
        object? payload = p.deserialize.Invoke(null, new object?[] {payloadMemory, null, null});
        return payload as Packet;
    }

}
