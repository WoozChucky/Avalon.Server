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
    private readonly int _bufferSize;
    private readonly ILogger<PacketReader> _logger;

    public int BufferSize => _bufferSize;

    private readonly Dictionary<NetworkPacketType, Func<ReadOnlyMemory<byte>, Packet?>> _packetTypes = new();

    public PacketReader(ILoggerFactory loggerFactory, IOptions<HostingConfiguration> options, Type[] packetTypes)
    {
        _logger = loggerFactory.CreateLogger<PacketReader>();
        _bufferSize = options.Value.PacketReaderBufferSize;

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
            _packetTypes.Add(networkPacketType, deserializer);
        }
    }

    public Packet? Read(InboundPacketFrame frame, DecryptFunc? decrypt = null)
    {
        // Not logged here: one reader serves every connection, and a peer can send these at any rate. The read loop
        // warns once per connection (#897).
        if (!_packetTypes.TryGetValue(frame.Header.Type, out Func<ReadOnlyMemory<byte>, Packet?>? deserializer))
            return null;

        byte[]? rented = null;

        try
        {
            ReadOnlyMemory<byte> payload;

            if (decrypt != null)
            {
                rented = ArrayPool<byte>.Shared.Rent(frame.Payload.Length);
                int len;
                try
                {
                    len = decrypt(frame.Payload.Span, rented);
                }
                // The two refusals of an open that the sender causes, each by its own type. A session that failed its
                // exchange or spent its counter refuses with a plain InvalidOperationException, and a cipher that
                // fails on a well-formed packet with a plain CryptographicException: both propagate.
                catch (CryptoSessionNotInitializedException e)
                {
                    throw new PacketReadException(PacketReadFailure.SealedBeforeKey, frame.Header.Type, e);
                }
                catch (CryptographicException e) when (e is AuthenticationTagMismatchException
                                                       || frame.Payload.Length < PacketEnvelope.SealOverhead)
                {
                    throw new PacketReadException(PacketReadFailure.SealNotAuthentic, frame.Header.Type, e);
                }

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
            if (rented != null) ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// What protobuf-net 3 throws for bytes that are not an encoding of the type asked for, measured over every
    /// client packet with mutated, truncated, random and deeply nested payloads (#897): its own
    /// <see cref="ProtoException" /> (a wire type or a group that does not fit), <see cref="EndOfStreamException" /> (a
    /// payload cut short), <see cref="OverflowException" /> (a varint too wide for its member),
    /// <see cref="InvalidOperationException" /> (a length past the payload's end, and nesting past the model's depth,
    /// whose stack trace alone is tens of kilobytes) and <see cref="ArgumentOutOfRangeException" /> (a date out of
    /// range). Anything else a decode throws (a packet class's own code failing, a missing type) is the server's and
    /// propagates. protobuf-net also reports a contract it cannot build as an InvalidOperationException; that is no
    /// runtime case, since the wire schema tests build every packet's contract.
    /// </summary>
    private static bool IsMalformedPayload(Exception e) =>
        e is ProtoException or EndOfStreamException or OverflowException or InvalidOperationException
            or ArgumentOutOfRangeException;

    private static Func<ReadOnlyMemory<byte>, Packet?> BuildDeserializer<T>() where T : Packet
        => static memory => Serializer.Deserialize<T>(memory) as Packet;
}
