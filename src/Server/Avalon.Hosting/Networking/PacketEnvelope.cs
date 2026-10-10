using System.Buffers;
using System.Diagnostics;
using Avalon.Common.Cryptography;
using Avalon.Network.Packets.Abstractions;

namespace Avalon.Hosting.Networking;

/// <summary>
/// Writes one frame as the client reads it (#875): <c>[varint length][NetworkPacket{1: header, 2: payload}]</c>, the
/// bytes protobuf-net writes for a <see cref="NetworkPacket" />, by hand, so no packet or header object is made per
/// frame. A sealed payload goes straight into the frame: its nonce, ciphertext and tag are written where they are sent.
/// </summary>
public static class PacketEnvelope
{
    /// <summary>What sealing adds to a payload: the 12-byte nonce in front, the 16-byte tag behind.</summary>
    public const int SealOverhead = SessionKeys.NonceSize + SessionKeys.TagSize;

    private const byte HeaderTag = (1 << 3) | 2;
    private const byte PayloadTag = (2 << 3) | 2;

    /// <summary>
    /// Appends <paramref name="packet" /> to <paramref name="burst" /> and returns the bytes written. A packet flagged
    /// Encrypted is sealed with <paramref name="sealer" />; with no sealer (a connection that sends plain inside TLS) it
    /// goes plain and its header loses the flag, since a client decrypts by the header. The payload is not released.
    /// </summary>
    public static int Append(IBufferWriter<byte> burst, in OutboundPacket packet, IAvalonCryptoSession? sealer)
    {
        NetworkPacketHeader header = packet.Header;
        bool seal = sealer is not null && (header.Flags & NetworkPacketFlags.Encrypted) != 0;
        if (!seal)
            header.Flags &= ~NetworkPacketFlags.Encrypted;

        ReadOnlySpan<byte> plain = packet.Payload is { } payload ? payload.Span : default;
        int payloadLength = seal ? plain.Length + SealOverhead : plain.Length;
        int headerLength = HeaderLength(header);

        Span<byte> frame = burst.GetSpan(EnvelopeLength(headerLength, payloadLength) + payloadLength);
        int at = WriteEnvelope(frame, header, headerLength, payloadLength);
        if (!seal)
        {
            plain.CopyTo(frame.Slice(at));
        }
        else
        {
            // From the segment's own buffer into the burst's: the two never overlap, as SealInto requires.
            int sealedLength = sealer!.SealInto(plain, frame.Slice(at, payloadLength));
            if (sealedLength != payloadLength)
            {
                Debug.Assert((uint)sealedLength < (uint)payloadLength,
                    "A sealer reported more bytes than it was given room for");
                at = Reframe(frame, header, headerLength, at, sealedLength);
            }

            payloadLength = sealedLength;
        }

        at += payloadLength;
        burst.Advance(at);
        return at;
    }

    /// <summary>
    /// Closes the frame around a seal shorter than the room made for it, which only the tests' pass-through sessions
    /// write (they add no nonce or tag). The payload moves down to just behind an envelope of its own length, which is
    /// never longer than the one written for the longer payload, and the envelope is written again in front of it.
    /// </summary>
    private static int Reframe(Span<byte> frame, in NetworkPacketHeader header, int headerLength, int sealedAt,
        int sealedLength)
    {
        int at = EnvelopeLength(headerLength, sealedLength);
        frame.Slice(sealedAt, sealedLength).CopyTo(frame.Slice(at));
        WriteEnvelope(frame, header, headerLength, sealedLength);
        return at;
    }

    /// <summary>The frame's bytes in front of a payload of <paramref name="payloadLength" />.</summary>
    private static int EnvelopeLength(int headerLength, int payloadLength)
    {
        int body = BodyLength(headerLength, payloadLength);
        return VarintLength((uint)body) + body - payloadLength;
    }

    private static int BodyLength(int headerLength, int payloadLength) =>
        1 + VarintLength((uint)headerLength) + headerLength + 1 + VarintLength((uint)payloadLength) + payloadLength;

    private static int WriteEnvelope(Span<byte> frame, in NetworkPacketHeader header, int headerLength,
        int payloadLength)
    {
        int at = WriteVarint(frame, (uint)BodyLength(headerLength, payloadLength));
        frame[at++] = HeaderTag;
        at += WriteVarint(frame.Slice(at), (uint)headerLength);
        at += WriteHeader(frame.Slice(at), header);
        frame[at++] = PayloadTag;
        at += WriteVarint(frame.Slice(at), (uint)payloadLength);
        return at;
    }

    // protobuf-net writes a header field only when it is not 0, each as an int32 varint, sign-extended to ten bytes
    // when negative (NetworkPacketType.ERROR is -1), in field order.
    private static int HeaderLength(in NetworkPacketHeader header) =>
        FieldLength((int)header.Type) + FieldLength((int)header.Flags) + FieldLength((int)header.Protocol)
        + FieldLength(header.Version);

    private static int FieldLength(int value) => value == 0 ? 0 : 1 + VarintLength(unchecked((ulong)(long)value));

    private static int WriteHeader(Span<byte> span, in NetworkPacketHeader header)
    {
        int at = WriteField(span, 1, (int)header.Type);
        at += WriteField(span.Slice(at), 2, (int)header.Flags);
        at += WriteField(span.Slice(at), 3, (int)header.Protocol);
        at += WriteField(span.Slice(at), 4, header.Version);
        return at;
    }

    private static int WriteField(Span<byte> span, int field, int value)
    {
        if (value == 0)
            return 0;

        span[0] = (byte)(field << 3);
        return 1 + WriteVarint(span.Slice(1), unchecked((ulong)(long)value));
    }

    private static int VarintLength(ulong value)
    {
        int length = 1;
        while (value > 0x7F)
        {
            value >>= 7;
            length++;
        }

        return length;
    }

    private static int WriteVarint(Span<byte> span, ulong value)
    {
        int i = 0;
        while (value > 0x7F)
        {
            span[i++] = (byte)((value & 0x7F) | 0x80);
            value >>= 7;
        }

        span[i++] = (byte)value;
        return i;
    }
}
