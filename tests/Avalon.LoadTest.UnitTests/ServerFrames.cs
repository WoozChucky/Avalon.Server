using System.Buffers;
using Avalon.Common.Cryptography;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.LoadTest.UnitTests;

/// <summary>A server packet as a bot receives it: framed, and sealed, by the server's own envelope, then read back.</summary>
internal static class ServerFrames
{
    public static NetworkPacket Received(OutboundPacket packet, IAvalonCryptoSession server)
    {
        var frame = new ArrayBufferWriter<byte>();
        try
        {
            PacketEnvelope.Append(frame, packet, server);
        }
        finally
        {
            packet.Release();
        }

        using var stream = new MemoryStream(frame.WrittenSpan.ToArray());
        return Serializer.DeserializeWithLengthPrefix<NetworkPacket>(stream, PrefixStyle.Base128);
    }
}
