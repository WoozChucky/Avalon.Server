using System.Buffers;
using Avalon.Common.Cryptography;
using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.LoadTest.Wire;

/// <summary>
/// Turns messages into packets and back, for one connection's session. Messages are serialized with protobuf-net
/// directly, as the server's own serialization helper is internal to the packet assembly.
/// </summary>
/// <remarks>
/// <see cref="Outgoing{T}"/> reuses one stream, so a connection calls it from one sender at a time, sealing or not.
/// <see cref="Decode{T}"/> may be called from any thread: the session opens on one thread at a time only (#875), and a
/// connection opens on two, its read loop and the bot that awaited a reply, so the codec serialises its opens itself.
/// </remarks>
public sealed class PacketCodec(IAvalonCryptoSession session)
{
    private readonly MemoryStream _stream = new(512);

    // Held only for the open, not the deserialisation: the session's receiving cipher is not thread-safe.
    private readonly Lock _openLock = new();

    /// <summary>
    /// Whether this connection seals what it sends, every packet but the pong, which the world takes plain: what the
    /// world's admission reply said (#875, <c>SGameAdmissionPacket.PacketEncryption</c>), set before anything else is
    /// sent. A world from before the flag never says, reads false, and opens each packet by its header.
    /// </summary>
    public bool Seals { get; set; }

    /// <summary>A packet for the world as its admission asked: sealed with the session's sending key, or plain.</summary>
    public NetworkPacket Outgoing<T>(T message, NetworkPacketType type) where T : class
    {
        _stream.SetLength(0);
        Serializer.Serialize(_stream, message);
        var written = new ReadOnlySpan<byte>(_stream.GetBuffer(), 0, (int)_stream.Length);
        return Seals
            ? new NetworkPacket { Header = Header(type, NetworkPacketFlags.Encrypted), Payload = session.Encryptor(written) }
            : new NetworkPacket { Header = Header(type, NetworkPacketFlags.None), Payload = written.ToArray() };
    }

    /// <summary>The message a packet carries, opened with the session's receiving key when the packet is encrypted.</summary>
    public T Decode<T>(NetworkPacket packet)
    {
        if ((packet.Header.Flags & NetworkPacketFlags.Encrypted) == 0)
            return Serializer.Deserialize<T>(new ReadOnlySpan<byte>(packet.Payload));

        byte[] plain = ArrayPool<byte>.Shared.Rent(packet.Payload.Length);
        try
        {
            int length;
            lock (_openLock)
                length = session.Decrypt(packet.Payload, plain);

            return Serializer.Deserialize<T>(new ReadOnlySpan<byte>(plain, 0, length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(plain);
        }
    }

    private static NetworkPacketHeader Header(NetworkPacketType type, NetworkPacketFlags flags) =>
        new() { Type = type, Flags = flags, Protocol = NetworkProtocol.Tcp, Version = 0 };
}
