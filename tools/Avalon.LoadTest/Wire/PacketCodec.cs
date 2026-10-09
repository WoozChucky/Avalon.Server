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
/// <see cref="Encrypted{T}"/> reuses one stream, so a connection calls it from one sender at a time.
/// </remarks>
public sealed class PacketCodec(IAvalonCryptoSession session)
{
    private readonly MemoryStream _stream = new(512);

    /// <summary>A packet sealed with the session's sending key.</summary>
    public NetworkPacket Encrypted<T>(T message, NetworkPacketType type) where T : class
    {
        _stream.SetLength(0);
        Serializer.Serialize(_stream, message);
        return new NetworkPacket
        {
            Header = Header(type, NetworkPacketFlags.Encrypted),
            Payload = session.Encryptor(new ReadOnlySpan<byte>(_stream.GetBuffer(), 0, (int)_stream.Length)),
        };
    }

    /// <summary>A packet whose payload is the serialized message as is, before or outside the session.</summary>
    public static NetworkPacket Clear<T>(T message, NetworkPacketType type, NetworkPacketFlags flags) where T : class
    {
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, message);
        return new NetworkPacket { Header = Header(type, flags), Payload = stream.ToArray() };
    }

    /// <summary>The message a packet carries, opened with the session's receiving key when the packet is encrypted.</summary>
    public T Decode<T>(NetworkPacket packet)
    {
        if ((packet.Header.Flags & NetworkPacketFlags.Encrypted) == 0)
            return Serializer.Deserialize<T>(new ReadOnlySpan<byte>(packet.Payload));

        byte[] plain = ArrayPool<byte>.Shared.Rent(packet.Payload.Length);
        try
        {
            int length = session.Decrypt(packet.Payload, plain);
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
