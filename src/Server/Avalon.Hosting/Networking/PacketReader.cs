using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Avalon.Configuration;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoBuf;

namespace Avalon.Hosting.Networking;

public delegate int DecryptFunc(ReadOnlySpan<byte> input, byte[] output);

public interface IPacketReader
{
    IAsyncEnumerable<NetworkPacket> EnumerateAsync(PacketStream stream, CancellationToken token = default);
    Packet? Read(NetworkPacket packet, DecryptFunc? decrypt = null);
}

public class PacketReader : IPacketReader
{
    private readonly int _bufferSize;
    private readonly ILogger<PacketReader> _logger;

    private readonly Dictionary<NetworkPacketType, Func<ReadOnlyMemory<byte>, Packet?>> _packetTypes = new();

    public PacketReader(ILoggerFactory loggerFactory, IOptions<HostingConfiguration> options, Type[] packetTypes)
    {
        _logger = loggerFactory.CreateLogger<PacketReader>();
        _bufferSize = options.Value.PacketReaderBufferSize;

        MethodInfo buildMethod = typeof(PacketReader)
            .GetMethod(nameof(BuildDeserializer), BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                $"Could not reflect {nameof(PacketReader)}.{nameof(BuildDeserializer)}. " +
                "Ensure the method is non-public, static, and not overloaded.");

        foreach (Type packetType in packetTypes)
        {
            FieldInfo? networkPacketTypeInfo = packetType.GetFields(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(field => field.FieldType == typeof(NetworkPacketType));
            if (networkPacketTypeInfo == null)
            {
                _logger.LogWarning("Packet type {PacketType} does not have a NetworkPacketType field", packetType);
                continue;
            }

            if (!typeof(Packet).IsAssignableFrom(packetType))
            {
                _logger.LogWarning("Packet type {PacketType} does not inherit Packet, skipping", packetType);
                continue;
            }

            NetworkPacketType networkPacketType = (NetworkPacketType)networkPacketTypeInfo.GetValue(null)!;
            var deserializer = (Func<ReadOnlyMemory<byte>, Packet?>)
                buildMethod.MakeGenericMethod(packetType).Invoke(null, null)!;
            _packetTypes.Add(networkPacketType, deserializer);
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

    public Packet? Read(NetworkPacket packet, DecryptFunc? decrypt = null)
    {
        if (!_packetTypes.TryGetValue(packet.Header.Type, out Func<ReadOnlyMemory<byte>, Packet?>? deserializer))
        {
            _logger.LogWarning("Unknown packet type {PacketType}", packet.Header.Type);
            return null;
        }

        byte[]? rented = null;

        try
        {
            ReadOnlyMemory<byte> payload;

            if (decrypt != null)
            {
                rented = ArrayPool<byte>.Shared.Rent(packet.Payload.Length);
                int len = decrypt(packet.Payload.AsSpan(), rented);
                payload = rented.AsMemory(0, len);
            }
            else
            {
                payload = new ReadOnlyMemory<byte>(packet.Payload);
            }

            return deserializer(payload);
        }
        finally
        {
            if (rented != null) ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private static Func<ReadOnlyMemory<byte>, Packet?> BuildDeserializer<T>() where T : Packet
        => static memory => Serializer.Deserialize<T>(memory) as Packet;

}
