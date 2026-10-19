using System.Buffers;
using System.Reflection;
using System.Security.Cryptography;
using Avalon.Common.Cryptography;
using Avalon.Configuration;
using Avalon.Network.Packets;
using Avalon.Network.Packets.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoBuf;
using ProtoBuf.Meta;

namespace Avalon.Hosting.Networking;

public delegate int DecryptFunc(ReadOnlySpan<byte> input, byte[] output);

public interface IPacketReader
{
    int BufferSize { get; }

    /// <summary>
    /// Opens the frame's payload with <paramref name="decrypt" />, when given one, and decodes it as the packet type
    /// its opcode names.
    /// </summary>
    /// <returns>
    /// The packet, or null when this server has no packet type for the opcode, and only then: nothing was opened or
    /// decoded, and what to say about it is the caller's, which knows the connection (#897).
    /// </returns>
    /// <exception cref="PacketReadException">
    /// The payload could not be opened or decoded, and the peer that sent it is why. Any other exception is not the
    /// peer's doing.
    /// </exception>
    Packet? Read(InboundPacketFrame frame, DecryptFunc? decrypt = null);
}

public class PacketReader : IPacketReader
{
    /// <summary>
    /// How many messages or groups deep a payload may nest before the decode refuses it (#897). protobuf-net's own
    /// limit is 512, and any peer reaches it, in any packet, with a few hundred bytes of groups opened inside one
    /// another in a field the packet does not have: the decode then recursed through all 512 before it threw, about
    /// half a millisecond and 130 KB on the read loop for each such frame. The deepest packet contract nests 5 (the
    /// quest log, which a server sends) and the deepest a server reads nests 2, so 32 is more than six times what any
    /// packet needs, and such a frame is refused in about a twentieth of the time.
    /// <para>
    /// The limit is the model's, and the process has one model: it bounds what the server writes as well. A packet
    /// that came to nest deeper would be refused when sent; <c>PacketReaderShould</c> holds every contract to a
    /// quarter of the limit.
    /// </para>
    /// </summary>
    public const int MaxDecodeDepth = 32;

    private readonly int _bufferSize;
    private readonly ILogger<PacketReader> _logger;

    public int BufferSize => _bufferSize;

    private readonly Dictionary<NetworkPacketType, Func<ReadOnlyMemory<byte>, Packet?>> _packetTypes = new();

    public PacketReader(ILoggerFactory loggerFactory, IOptions<HostingConfiguration> options, Type[] packetTypes)
    {
        _logger = loggerFactory.CreateLogger<PacketReader>();
        _bufferSize = options.Value.PacketReaderBufferSize;

        // Before any decoder is built or used, and a server builds its reader before it prepares what it sends
        // (PacketEncoder.PrepareServerPackets), so before any serializer too. Every reader sets the same value, so in
        // effect it is set once. protobuf-net reads the limit as it reads and writes, not as it builds, so a type
        // built before this line (by a test or a tool that encodes first) is bound by it from here on as well.
        RuntimeTypeModel.Default.MaxDepth = MaxDecodeDepth;

        MethodInfo buildMethod = typeof(PacketReader)
            .GetMethod(nameof(BuildDeserializer), BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException(
                $"Could not reflect {nameof(PacketReader)}.{nameof(BuildDeserializer)}. " +
                "Ensure the method is non-public, static, and not overloaded.");

        foreach (Type packetType in packetTypes)
        {
            FieldInfo? networkPacketTypeInfo = packetType.GetFields(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(field => field.FieldType == typeof(NetworkPacketType));
            if (networkPacketTypeInfo == null)
            {
                _logger.LogWarning("Packet type {PacketType} does not have a NetworkPacketType field", packetType);
                continue;
            }

            if (!typeof(Packet).IsAssignableFrom(packetType))
            {
                _logger.LogWarning("Packet type {PacketType} does not inherit Packet, skipping", packetType);
                continue;
            }

            var networkPacketType = (NetworkPacketType)networkPacketTypeInfo.GetValue(null)!;
            var deserializer = (Func<ReadOnlyMemory<byte>, Packet?>)
                buildMethod.MakeGenericMethod(packetType).Invoke(null, null)!;
            Prepare(packetType, deserializer);
            _packetTypes.Add(networkPacketType, deserializer);
        }
    }

    /// <summary>
    /// Decodes an empty payload as <paramref name="packetType" />, once, as the reader is built (#897). protobuf-net
    /// builds a type's decoder on its first use, and a contract it cannot build fails every decode with an
    /// <see cref="InvalidOperationException" />, the type it also throws for malformed bytes: left to the first frame,
    /// that server defect would close every client that sends the opcode and blame its payload
    /// (<see cref="IsMalformedPayload" />). Here it stops the reader from being built, and so the server from
    /// starting, which builds its reader before it opens its port. It also moves the build of each decoder (a
    /// millisecond or two a type, and some seventy more for the first in a process) from the read loop that
    /// received the first packet of the type to startup. What a server sends is built the same way, by
    /// <c>PacketEncoder.PrepareServerPackets</c> (#883).
    /// </summary>
    private static void Prepare(Type packetType, Func<ReadOnlyMemory<byte>, Packet?> deserializer)
    {
        try
        {
            deserializer(ReadOnlyMemory<byte>.Empty);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(
                $"Packet type {packetType.FullName} cannot be decoded: protobuf-net could not build its contract, or " +
                "could not create it. A server does not start with a packet it could never read.", e);
        }
    }

    public Packet? Read(InboundPacketFrame frame, DecryptFunc? decrypt = null)
    {
        // Not logged here: one reader serves every connection, and a peer can send these at any rate. The read loop
        // warns once per connection (#897).
        if (!_packetTypes.TryGetValue(frame.Header.Type, out Func<ReadOnlyMemory<byte>, Packet?>? deserializer))
            return null;

        byte[]? rented = null;
        int opened = 0;

        try
        {
            ReadOnlyMemory<byte> payload;

            if (decrypt != null)
            {
                rented = ArrayPool<byte>.Shared.Rent(frame.Payload.Length);
                // Until the open has said how much it wrote: no more than it was given, and an open that fails may
                // have written part of it first.
                opened = frame.Payload.Length;
                int len;
                try
                {
                    len = decrypt(frame.Payload.Span, rented);
                }
                // The two refusals of an open that the sender causes, each by its own type: no exchange completed
                // (none yet, none on this connection, or one that failed on the peer's key, #878), and a seal that does
                // not open. A session that spent its counter refuses with a plain InvalidOperationException, and a
                // cipher that fails on a well-formed packet with a plain CryptographicException: both propagate.
                catch (CryptoSessionNotInitializedException e)
                {
                    throw new PacketReadException(PacketReadFailure.SealedBeforeKey, frame.Header.Type, e);
                }
                catch (CryptographicException e) when (e is AuthenticationTagMismatchException
                                                       || frame.Payload.Length < PacketEnvelope.SealOverhead)
                {
                    throw new PacketReadException(PacketReadFailure.SealNotAuthentic, frame.Header.Type, e);
                }

                opened = len;
                payload = rented.AsMemory(0, len);
            }
            else
            {
                payload = frame.Payload;
            }

            try
            {
                return deserializer(payload);
            }
            catch (Exception e) when (IsMalformedPayload(e))
            {
                throw new PacketReadException(PacketReadFailure.Undecodable, frame.Header.Type, e);
            }
        }
        finally
        {
            if (rented != null)
            {
                // The opened payload is cleared before its buffer goes back to the pool, which hands the array to
                // whoever rents next. Nothing that rents
                // reads before it writes, so this is a second line and not a fix. Only what was written is cleared,
                // not the whole array: a frame is at most Hosting:MaxInboundFrameSize, and most are tens of bytes.
                rented.AsSpan(0, opened).Clear();
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>
    /// What protobuf-net 3 throws for bytes that are not an encoding of the type asked for, measured over every
    /// client packet with mutated, truncated, random and deeply nested payloads (#897): its own
    /// <see cref="ProtoException" /> (a wire type or a group that does not fit), <see cref="EndOfStreamException" /> (a
    /// payload cut short), <see cref="OverflowException" /> (a varint too wide for its member),
    /// <see cref="InvalidOperationException" /> (a length past the payload's end, and nesting past
    /// <see cref="MaxDecodeDepth" />) and <see cref="ArgumentOutOfRangeException" /> (a date out of
    /// range). Anything else a decode throws is the server's and propagates. protobuf-net also reports a contract it
    /// cannot build as an InvalidOperationException, on every decode; that never reaches a frame, since the reader
    /// decodes once with every type as it is built (<see cref="Prepare" />).
    /// </summary>
    private static bool IsMalformedPayload(Exception e) =>
        e is ProtoException or EndOfStreamException or OverflowException or InvalidOperationException
            or ArgumentOutOfRangeException;

    private static Func<ReadOnlyMemory<byte>, Packet?> BuildDeserializer<T>() where T : Packet
        => static memory => Serializer.Deserialize<T>(memory) as Packet;
}
