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
    IAsyncEnumerable<NetworkPacket> EnumerateAsync(Stream stream, CancellationToken token = default);
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

    public async IAsyncEnumerable<NetworkPacket> EnumerateAsync(Stream stream,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(_bufferSize);

        try
        {
            while (true)
            {
                // read packet size
                int packetSize;
                int offset = 0;

                try
                {
                    (packetSize, offset) = await ReadVarintAsync(stream, buffer, token);
                }
                catch (ObjectDisposedException)
                {
                    _logger.LogDebug("Connection was disposed while waiting or reading new packages. This may be fine");
                    break;
                }
                catch (IOException)
                {
                    _logger.LogDebug("Connection was most likely closed while reading a packet size. This may be fine");
                    break;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug("Operation was canceled while reading a packet size. This may be fine");
                    break;
                }

                if (packetSize > _bufferSize)
                {
                    _logger.LogError("Packet size {PacketSize} exceeds buffer capacity {BufferSize}", packetSize,
                        _bufferSize);
                    break;
                }

                // read packet contents
                try
                {
                    await stream.ReadExactlyAsync(buffer.AsMemory(offset, packetSize), token);
                }
                catch (ObjectDisposedException)
                {
                    _logger.LogDebug("Connection was disposed while waiting or reading new packages. This may be fine");
                    break;
                }
                catch (IOException)
                {
                    _logger.LogDebug("Connection was most likely closed while reading a packet. This may be fine");
                    break;
                }

                Memory<byte> packetBuffer = buffer.AsMemory(offset, packetSize);

                yield return NetworkPacket.Deserialize(packetBuffer);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
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

    private async ValueTask<(int packetSize, int offset)> ReadVarintAsync(Stream stream, byte[] buffer,
        CancellationToken token = default)
    {
        int packetSize = 0;
        int shift = 0;
        int index = 0;

        while (true)
        {
            // Ensure buffer has enough room, since we're reading one byte at a time
            if (index >= buffer.Length)
            {
                throw new InvalidOperationException("Buffer overflow when reading varint.");
            }

            // Read one byte
            int bytesRead = await stream.ReadAsync(buffer.AsMemory(index, 1), token);
            if (bytesRead == 0)
            {
                throw new EndOfStreamException("Stream ended while reading varint.");
            }

            // Extract the 7 least significant bits
            byte currentByte = buffer[index];
            packetSize |= (currentByte & 0x7F) << shift;

            // Check MSB to see if more bytes are part of the size
            if ((currentByte & 0x80) == 0)
            {
                return (packetSize, index + 1);
            }

            // Move to the next byte
            shift += 7;
            index++;
        }
    }
}
