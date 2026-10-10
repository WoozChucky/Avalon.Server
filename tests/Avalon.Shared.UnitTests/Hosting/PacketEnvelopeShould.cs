using System.Buffers;
using Avalon.Common.Cryptography;
using Avalon.Hosting.Networking;
using Avalon.Network.Packets.Abstractions;
using Avalon.Network.Packets.Serialization;
using Org.BouncyCastle.Crypto;
using ProtoBuf;
using Xunit;

namespace Avalon.Shared.UnitTests.Hosting;

/// <summary>
/// The send path writes each frame's envelope by hand (#875): <c>[varint length][NetworkPacket{1: header, 2: payload}]</c>,
/// with no NetworkPacket or header object. The client reads it through protobuf-net's contract, so every byte must be
/// the byte protobuf-net writes for the same packet, sealed or plain.
/// </summary>
public class PacketEnvelopeShould
{
    /// <summary>What the connection seals with.</summary>
    public enum Sealer
    {
        /// <summary>Nothing: the connection sends plain inside TLS.</summary>
        None,

        /// <summary>A real session, which adds a nonce and a tag.</summary>
        Session,

        /// <summary>The test fakes' session, which seals a payload to itself: nothing added.</summary>
        PassThrough,
    }

    public static TheoryData<int, NetworkPacketType, NetworkPacketFlags, Sealer> Frames() => new()
    {
        // payload length, type, flags as created, what the connection seals with
        { 0, NetworkPacketType.SMSG_DISCONNECT, NetworkPacketFlags.None, Sealer.Session },
        { 1, NetworkPacketType.SMSG_PING, NetworkPacketFlags.None, Sealer.Session },
        { 127, NetworkPacketType.SMSG_GAME_ADMISSION, NetworkPacketFlags.ClearText, Sealer.Session },
        { 128, NetworkPacketType.SMSG_WORLD_STATE_UPDATE, NetworkPacketFlags.Encrypted, Sealer.Session },
        { 100, NetworkPacketType.SMSG_WORLD_STATE_UPDATE, NetworkPacketFlags.Encrypted, Sealer.None },
        { 7_000, NetworkPacketType.SMSG_WORLD_STATE_ADD, NetworkPacketFlags.Encrypted, Sealer.Session },
        { 70_000, NetworkPacketType.SMSG_WORLD_STATE_ADD, NetworkPacketFlags.Encrypted, Sealer.None },
        // A negative type: protobuf-net writes it as a ten-byte varint.
        { 3, NetworkPacketType.ERROR, NetworkPacketFlags.Encrypted, Sealer.Session },
        // A sealer that writes less than the envelope made room for: the frame closes around what it wrote. 100 bytes,
        // where a real seal would write 128, so the payload's length varint is a byte shorter.
        { 100, NetworkPacketType.SMSG_WORLD_STATE_UPDATE, NetworkPacketFlags.Encrypted, Sealer.PassThrough },
    };

    /// <summary>
    /// A packet flagged Encrypted on a connection that seals goes sealed, under the flag; on one that does not (TLS
    /// only), plain and without the flag, since the client decrypts by the header. Every other packet goes as it is.
    /// </summary>
    [Theory]
    [MemberData(nameof(Frames))]
    public void Write_the_frame_protobuf_net_writes(int payloadLength, NetworkPacketType type, NetworkPacketFlags flags,
        Sealer sealer)
    {
        byte[] payload = new byte[payloadLength];
        new Random(payloadLength).NextBytes(payload);
        var pool = new PayloadSegmentPool();
        var packet = new OutboundPacket(
            new NetworkPacketHeader { Type = type, Flags = flags, Protocol = NetworkProtocol.Tcp }, pool.Rent(payload));
        (IAvalonCryptoSession writer, IAvalonCryptoSession reference) = TwinSessions();

        bool seals = sealer != Sealer.None && (flags & NetworkPacketFlags.Encrypted) != 0;
        var expectedPacket = new NetworkPacket
        {
            Header = new NetworkPacketHeader
            {
                Type = type,
                Flags = seals ? flags : flags & ~NetworkPacketFlags.Encrypted,
                Protocol = NetworkProtocol.Tcp,
            },
            Payload = seals && sealer == Sealer.Session ? reference.Encrypt(payload) : payload,
        };
        using var expected = new MemoryStream();
        Serializer.SerializeWithLengthPrefix(expected, expectedPacket, PrefixStyle.Base128);

        IAvalonCryptoSession? connectionSealer = sealer switch
        {
            Sealer.Session => writer,
            Sealer.PassThrough => new PassThroughSession(),
            _ => null,
        };
        var burst = new ArrayBufferWriter<byte>();
        int written = PacketEnvelope.Append(burst, packet, connectionSealer);

        Assert.Equal(expected.ToArray(), burst.WrittenSpan.ToArray());
        Assert.Equal(burst.WrittenCount, written);
        packet.Release();
        Assert.Equal(0, pool.Outstanding);
    }

    /// <summary>The frame goes into the connection's reused buffer, sealed in place: nothing per packet (#875).</summary>
    [Fact]
    public void Frame_and_seal_a_packet_without_allocating()
    {
        var pool = new PayloadSegmentPool();
        var packet = new OutboundPacket(
            new NetworkPacketHeader
            {
                Type = NetworkPacketType.SMSG_WORLD_STATE_UPDATE,
                Flags = NetworkPacketFlags.Encrypted,
                Protocol = NetworkProtocol.Tcp,
            },
            pool.Rent(new byte[7_000]));
        (IAvalonCryptoSession session, _) = TwinSessions();
        using var burst = new PooledArrayBufferWriter();
        PacketEnvelope.Append(burst, packet, session);

        // The fewest bytes over three windows, as WaypointRepathAllocationShould takes them.
        long fewest = long.MaxValue;
        for (int window = 0; window < 3; window++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int frame = 0; frame < 100; frame++)
            {
                burst.Reset();
                PacketEnvelope.Append(burst, packet, session);
            }

            fewest = Math.Min(fewest, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(0, fewest);
    }

    /// <summary>Two server sessions over one key pair and one peer key: they seal the same packets to the same bytes.</summary>
    private static (IAvalonCryptoSession, IAvalonCryptoSession) TwinSessions()
    {
        AsymmetricCipherKeyPair server = AsymmetricCipher.GenerateECDHKeyPair(256);
        byte[] client = AsymmetricCipher.GetPublicKeyBytes(
            AsymmetricCipher.GetPublicKeyFromKeyPair(AsymmetricCipher.GenerateECDHKeyPair(256)));
        var first = new AvalonCryptoSession(CryptoRole.Server, server);
        var second = new AvalonCryptoSession(CryptoRole.Server, server);
        first.Initialize(client);
        second.Initialize(client);
        return (first, second);
    }

    /// <summary>
    /// The sealing of the world and auth tests' FakeAvalonCryptoSession, which this project cannot reference: the
    /// plaintext as it is, its own length returned.
    /// </summary>
    private sealed class PassThroughSession : IAvalonCryptoSession
    {
        public EncryptFunc Encryptor => throw new NotSupportedException();

        public int SealInto(ReadOnlySpan<byte> plaintext, Span<byte> destination)
        {
            plaintext.CopyTo(destination);
            return plaintext.Length;
        }

        public void Initialize(byte[] otherEndPublicKeyBytes) => throw new NotSupportedException();
        public byte[] GetPublicKey() => throw new NotSupportedException();
        public byte[] GetOtherEndPublicKey() => throw new NotSupportedException();
        public byte[] Encrypt(ReadOnlySpan<byte> data) => throw new NotSupportedException();
        public int Decrypt(ReadOnlySpan<byte> data, byte[] output) => throw new NotSupportedException();
        public byte[] GenerateHandshakeData() => throw new NotSupportedException();
    }
}
