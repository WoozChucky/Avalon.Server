// Licensed to the Avalon ARPG Game under one or more agreements.
// Avalon ARPG Game licenses this file to you under the MIT license.
using System;
using System.IO;
using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Hosting.Networking;

/// <summary>
/// Inbound-only frame produced by <see cref="PacketStream.EnumerateAsync"/>.
/// <para><b>Lifetime:</b> <see cref="Payload"/> is a zero-copy slice of the stream's
/// rented buffer. It is only valid until the enumerator advances to the next packet —
/// consume it within the same loop iteration and do not store it.</para>
/// </summary>
public readonly struct InboundPacketFrame
{
    public NetworkPacketHeader Header { get; }
    public ReadOnlyMemory<byte> Payload { get; }
    public int Size => Header.Size + Payload.Length;

    public InboundPacketFrame(NetworkPacketHeader header, ReadOnlyMemory<byte> payload)
    {
        Header = header;
        Payload = payload;
    }

    /// <summary>
    /// Parses a raw protobuf-encoded NetworkPacket frame without allocating a byte[]
    /// for the payload. Field order is fixed (field 1 = header, field 2 = payload)
    /// because we own both ends of the wire.
    /// </summary>
    public static InboundPacketFrame ParseFrame(ReadOnlyMemory<byte> buffer)
    {
        ReadOnlySpan<byte> span = buffer.Span;
        int pos = 0;
        NetworkPacketHeader header = new();
        ReadOnlyMemory<byte> payload = ReadOnlyMemory<byte>.Empty;

        while (pos < span.Length)
        {
            int tag = ReadVarint(span, ref pos);
            int fieldNumber = tag >> 3;
            int wireType = tag & 0x07;
            if (wireType != 2)
            {
                // Skip non-LEN-typed fields gracefully (forward-compatibility with schema additions).
                switch (wireType)
                {
                    case 0: ReadVarint(span, ref pos); break;          // varint
                    case 1: pos += 8; break;                           // 64-bit
                    case 5: pos += 4; break;                           // 32-bit
                    default: throw new InvalidDataException(
                        $"Unsupported protobuf wire type {wireType} for field {fieldNumber} in NetworkPacket frame.");
                }
                continue;
            }

            int len = ReadVarint(span, ref pos);

            if (fieldNumber == 1)
                header = Serializer.Deserialize<NetworkPacketHeader>(buffer.Slice(pos, len));
            else if (fieldNumber == 2)
                payload = buffer.Slice(pos, len);
            // else: skip unknown field — pos += len advances past it

            pos += len;
        }

        return new InboundPacketFrame(header, payload);
    }

    private static int ReadVarint(ReadOnlySpan<byte> span, ref int pos)
    {
        int result = 0;
        int shift = 0;
        byte b;
        do
        {
            b = span[pos++];
            result |= (b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);
        return result;
    }
}
