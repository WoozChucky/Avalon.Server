using Avalon.Network.Packets.Abstractions;

namespace Avalon.Hosting.Networking;

/// <summary>
/// Inbound-only frame produced by <see cref="PacketStream.EnumerateRawFramesAsync"/>.
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
    /// Parses a raw protobuf-encoded NetworkPacket frame without allocating: the payload is a slice of the buffer,
    /// and the header is read field by field (#875), since protobuf-net allocates for a struct it deserializes.
    /// Field order is fixed (field 1 = header, field 2 = payload) because we own both ends of the wire.
    /// </summary>
    public static InboundPacketFrame ParseFrame(ReadOnlyMemory<byte> buffer)
    {
        ReadOnlySpan<byte> span = buffer.Span;
        int pos = 0;
        NetworkPacketHeader header = default;
        ReadOnlyMemory<byte> payload = ReadOnlyMemory<byte>.Empty;

        while (pos < span.Length)
        {
            ulong tag = ReadVarint(span, ref pos);
            int fieldNumber = (int)(tag >> 3);
            int wireType = (int)(tag & 0x07);
            if (wireType != 2)
            {
                // Skip non-LEN-typed fields gracefully (forward-compatibility with schema additions).
                SkipField(span, ref pos, wireType, fieldNumber);
                continue;
            }

            int len = checked((int)ReadVarint(span, ref pos));

            if (fieldNumber == 1)
                header = ReadHeader(span.Slice(pos, len));
            else if (fieldNumber == 2)
                payload = buffer.Slice(pos, len);
            // else: skip unknown field — pos += len advances past it

            pos += len;
        }

        return new InboundPacketFrame(header, payload);
    }

    /// <summary>The header's four fields, each an int32 varint (sign-extended to ten bytes when negative).</summary>
    private static NetworkPacketHeader ReadHeader(ReadOnlySpan<byte> span)
    {
        NetworkPacketHeader header = default;
        int pos = 0;
        while (pos < span.Length)
        {
            ulong tag = ReadVarint(span, ref pos);
            int fieldNumber = (int)(tag >> 3);
            int wireType = (int)(tag & 0x07);
            if (wireType != 0)
            {
                SkipField(span, ref pos, wireType, fieldNumber);
                continue;
            }

            int value = unchecked((int)ReadVarint(span, ref pos));
            switch (fieldNumber)
            {
                case 1: header.Type = (NetworkPacketType)value; break;
                case 2: header.Flags = (NetworkPacketFlags)value; break;
                case 3: header.Protocol = (NetworkProtocol)value; break;
                case 4: header.Version = value; break;
            }
        }

        return header;
    }

    private static void SkipField(ReadOnlySpan<byte> span, ref int pos, int wireType, int fieldNumber)
    {
        switch (wireType)
        {
            case 0: ReadVarint(span, ref pos); break;                          // varint
            case 1: pos += 8; break;                                           // 64-bit
            case 2: pos += checked((int)ReadVarint(span, ref pos)); break;     // length-delimited
            case 5: pos += 4; break;                                           // 32-bit
            default:
                throw new InvalidDataException(
                    $"Unsupported protobuf wire type {wireType} for field {fieldNumber} in NetworkPacket frame.");
        }
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> span, ref int pos)
    {
        ulong result = 0;
        int shift = 0;
        byte b;
        do
        {
            if (shift > 63)
                throw new InvalidDataException("A varint in a NetworkPacket frame runs past ten bytes.");
            b = span[pos++];
            result |= (ulong)(b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);
        return result;
    }
}
