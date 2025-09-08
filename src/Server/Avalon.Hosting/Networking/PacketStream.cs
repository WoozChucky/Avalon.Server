// Licensed to the Avalon MMORPG Game under one or more agreements.
// Avalon MMORPG Game licenses this file to you under the MIT license.

using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Avalon.Hosting.Networking;

using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;

public class PacketStream(Stream stream) : Stream
{
    public override void Flush() => stream.Flush();

    public override int Read(byte[] buffer, int offset, int count) => stream.Read(buffer, offset, count);

    public override long Seek(long offset, SeekOrigin origin) => stream.Seek(offset, origin);

    public override void SetLength(long value) => stream.SetLength(value);

    public override void Write(byte[] buffer, int offset, int count) => stream.Write(buffer, offset, count);

    public override bool CanRead => stream.CanRead;
    public override bool CanSeek => stream.CanSeek;
    public override bool CanWrite => stream.CanWrite;
    public override long Length => stream.Length;
    public override long Position { get => stream.Position; set => stream.Position = value; }

    public async Task<(int packetSize, int offset)> ReadVarIntAsync(byte[] buffer, CancellationToken token = default)
    {
        int packetSize = 0;
        int shift = 0;
        int index = 0;

        while (true)
        {
            if (index >= buffer.Length)
            {
                throw new InvalidOperationException("Buffer overflow when reading varint.");
            }

            int bytesRead = await stream.ReadAsync(buffer.AsMemory(index, 1), token);
            if (bytesRead == 0)
            {
                throw new EndOfStreamException("Stream ended while reading varint.");
            }

            byte currentByte = buffer[index];
            packetSize |= (currentByte & 0x7F) << shift;

            if ((currentByte & 0x80) == 0)
            {
                return (packetSize, index + 1);
            }

            shift += 7;
            index++;
        }

    }

    public async Task ReadExactlyAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int bytesRead = await stream.ReadAsync(buffer[totalRead..], token);
            if (bytesRead == 0)
            {
                throw new EndOfStreamException("Stream ended before reading the expected number of bytes.");
            }
            totalRead += bytesRead;
        }
    }

    // New: Efficient enumerator that yields NetworkPacket with minimal allocations
    public async IAsyncEnumerable<NetworkPacket> EnumerateAsync(int initialBufferSize = 4096, [EnumeratorCancellation] CancellationToken token = default)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(initialBufferSize);
        try
        {
            while (true)
            {
                int offset = 0;
                int packetSize;
                try
                {
                    (packetSize, offset) = await ReadVarIntAsync(buffer, token);
                }
                catch (ObjectDisposedException)
                {
                    yield break;
                }
                catch (IOException)
                {
                    yield break;
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }

                if (packetSize + offset > buffer.Length)
                {
                    // Need a larger buffer for this packet; rent a bigger one
                    int newSize = Math.Max(buffer.Length * 2, packetSize + offset);
                    byte[] newBuffer = ArrayPool<byte>.Shared.Rent(newSize);
                    // copy varint bytes already in buffer [0..offset)
                    buffer.AsSpan(0, offset).CopyTo(newBuffer);
                    ArrayPool<byte>.Shared.Return(buffer);
                    buffer = newBuffer;
                }

                try
                {
                    await ReadExactlyAsync(buffer.AsMemory(offset, packetSize), token);
                }
                catch (ObjectDisposedException)
                {
                    yield break;
                }
                catch (IOException)
                {
                    yield break;
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

    // New: Decrypt helper that can work in-place if decrypt supports it
    public void Decrypt(NetworkPacket packet, Func<ReadOnlySpan<byte>, IBufferWriter<byte>, ReadOnlyMemory<byte>>? spanDecrypt = null, Func<byte[], byte[]>? arrayDecrypt = null)
    {
        if (spanDecrypt is not null)
        {
            var rented = new ArrayBufferWriter<byte>(packet.Payload.Length);
            var result = spanDecrypt(packet.Payload, rented);
            if (!result.IsEmpty)
            {
                packet.Payload = result.ToArray();
            }
            else
            {
                packet.Payload = rented.WrittenMemory.ToArray();
            }
        }
        else if (arrayDecrypt is not null)
        {
            packet.Payload = arrayDecrypt(packet.Payload);
        }
        // else: no decryption
    }

    // New: Deserialize helper using provided map
    public Packet? DeserializePayload(NetworkPacket packet, IReadOnlyDictionary<NetworkPacketType, (Type type, System.Reflection.MethodInfo method)> map)
    {
        if (!map.TryGetValue(packet.Header.Type, out var p))
        {
            return null;
        }
        ReadOnlyMemory<byte> payloadMemory = new(packet.Payload);
        object? payload = p.method.Invoke(null, new object?[] { payloadMemory, null, null });
        return payload as Packet;
    }

}
