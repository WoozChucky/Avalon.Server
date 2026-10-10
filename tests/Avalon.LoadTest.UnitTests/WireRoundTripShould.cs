using System.Buffers;
using Avalon.Common.Cryptography;
using Avalon.Hosting.Networking;
using Avalon.LoadTest.Wire;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Movement;
using Avalon.Network.Packets.Serialization;
using Org.BouncyCastle.Crypto;
using ProtoBuf;
using Xunit;

namespace Avalon.LoadTest.UnitTests;

public class WireRoundTripShould
{
    /// <summary>
    /// The bot's codec and framing against the server's own reader and envelope (#875), in both of the world's modes:
    /// sealed both ways (Network:PacketEncryption on), or plain inside TLS (off), as the admission reply says.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Carry_client_and_server_packets_through_the_same_framing_and_keys(bool packetEncryption)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken ct = timeout.Token;

        // Admission-style exchange: the client sends its key before it has the server's.
        AsymmetricCipherKeyPair clientKeys = AsymmetricCipher.GenerateECDHKeyPair();
        byte[] clientPublic = AsymmetricCipher.GetPublicKeyBytes(AsymmetricCipher.GetPublicKeyFromKeyPair(clientKeys));
        var client = new AvalonCryptoSession(CryptoRole.Client, clientKeys);
        var server = new AvalonCryptoSession(CryptoRole.Server);
        server.Initialize(clientPublic);
        client.Initialize(server.GetPublicKey());
        var codec = new PacketCodec(client) { Seals = packetEncryption };

        // Client → server: many frames through the bot's writer, read by the server's own frame loop, opened by header.
        var wire = new MemoryStream();
        var writer = new FrameWriter(wire);
        for (uint seq = 1; seq <= 500; seq++)
            await writer.WriteAsync(codec.Outgoing(new CPlayerInputPacket { Seq = seq, DirX = 1 }, NetworkPacketType.CMSG_PLAYER_INPUT), ct);
        wire.Position = 0;
        uint expected = 1;
        await foreach (ReadOnlyMemory<byte> raw in new PacketStream(wire).EnumerateRawFramesAsync(256, ct))
        {
            var frame = InboundPacketFrame.ParseFrame(raw);
            Assert.Equal(NetworkPacketType.CMSG_PLAYER_INPUT, frame.Header.Type);
            Assert.Equal(packetEncryption, (frame.Header.Flags & NetworkPacketFlags.Encrypted) != 0);
            byte[] plain = new byte[frame.Payload.Length];
            int length = packetEncryption ? server.Decrypt(frame.Payload.Span, plain) : Copy(frame.Payload, plain);
            CPlayerInputPacket input = Serializer.Deserialize<CPlayerInputPacket>(plain.AsSpan(0, length));
            Assert.Equal(expected++, input.Seq);
        }
        Assert.Equal(501u, expected);

        // Server → client: the server's own factories and envelope, sealed or plain as the world is, read by the bot.
        IAvalonCryptoSession? sealer = packetEncryption ? server : null;
        var back = new ArrayBufferWriter<byte>();
        PacketEnvelope.Append(back, SPlayerStateAckPacket.Create(7, 1, 2, 3, 0, 0, 90, PacketEncoder.Shared), sealer);
        PacketEnvelope.Append(back, SGameAdmissionPacket.Create(server.GetPublicKey(), PacketEncoder.Shared,
            packetEncryption: packetEncryption), sealer);
        var reader = new FrameReader(new MemoryStream(back.WrittenSpan.ToArray()));
        SPlayerStateAckPacket ack = codec.Decode<SPlayerStateAckPacket>((await reader.ReadAsync(ct))!);
        Assert.Equal(7u, ack.Seq);
        SGameAdmissionPacket admission = codec.Decode<SGameAdmissionPacket>((await reader.ReadAsync(ct))!);
        Assert.Equal(GameAdmissionResult.Accepted, admission.Result);
        Assert.Equal(packetEncryption, admission.PacketEncryption);
        Assert.Null(await reader.ReadAsync(ct));
    }

    private static int Copy(ReadOnlyMemory<byte> from, byte[] to)
    {
        from.Span.CopyTo(to);
        return from.Length;
    }
}
