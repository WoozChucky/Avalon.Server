using Avalon.Common.Cryptography;
using Avalon.Hosting.Networking;
using Avalon.LoadTest.Wire;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Auth;
using Avalon.Network.Packets.Movement;
using Org.BouncyCastle.Crypto;
using ProtoBuf;
using Xunit;

namespace Avalon.LoadTest.UnitTests;

public class WireRoundTripShould
{
    [Fact]
    public async Task Carry_client_and_server_packets_through_the_same_framing_and_keys()
    {
        // Admission-style exchange: the client sends its key before it has the server's.
        AsymmetricCipherKeyPair clientKeys = AsymmetricCipher.GenerateECDHKeyPair();
        byte[] clientPublic = AsymmetricCipher.GetPublicKeyBytes(AsymmetricCipher.GetPublicKeyFromKeyPair(clientKeys));
        var client = new AvalonCryptoSession(CryptoRole.Client, clientKeys);
        var server = new AvalonCryptoSession(CryptoRole.Server);
        server.Initialize(clientPublic);
        client.Initialize(server.GetPublicKey());
        var codec = new PacketCodec(client);

        // Client → server: many frames through the bot's writer, read by the server's own frame loop.
        var wire = new MemoryStream();
        for (uint seq = 1; seq <= 500; seq++)
            await FrameIo.WriteAsync(wire, codec.Encrypted(new CPlayerInputPacket { Seq = seq, DirX = 1 }, NetworkPacketType.CMSG_PLAYER_INPUT), default);
        wire.Position = 0;
        uint expected = 1;
        await foreach (ReadOnlyMemory<byte> raw in new PacketStream(wire).EnumerateRawFramesAsync(256))
        {
            var frame = InboundPacketFrame.ParseFrame(raw);
            Assert.Equal(NetworkPacketType.CMSG_PLAYER_INPUT, frame.Header.Type);
            byte[] plain = new byte[frame.Payload.Length];
            int length = server.Decrypt(frame.Payload.Span, plain);
            CPlayerInputPacket input = Serializer.Deserialize<CPlayerInputPacket>(plain.AsSpan(0, length));
            Assert.Equal(expected++, input.Seq);
        }
        Assert.Equal(501u, expected);

        // Server → client: the server's own Create helpers, read by the bot's reader and codec.
        var back = new MemoryStream();
        Serializer.SerializeWithLengthPrefix(back, SPlayerStateAckPacket.Create(7, 1, 2, 3, 0, 0, 90, server.Encryptor), PrefixStyle.Base128);
        Serializer.SerializeWithLengthPrefix(back, SGameAdmissionPacket.Create(server.GetPublicKey()), PrefixStyle.Base128);
        back.Position = 0;
        var reader = new FrameReader(back);
        SPlayerStateAckPacket ack = codec.Decode<SPlayerStateAckPacket>((await reader.ReadAsync(default))!);
        Assert.Equal(7u, ack.Seq);
        SGameAdmissionPacket admission = codec.Decode<SGameAdmissionPacket>((await reader.ReadAsync(default))!);
        Assert.Equal(GameAdmissionResult.Accepted, admission.Result);
        Assert.Null(await reader.ReadAsync(default));
    }
}
