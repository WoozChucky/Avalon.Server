using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Avalon.Network.Packets.Abstractions;
using ProtoBuf;

namespace Avalon.Network.Packets.Serialization;

/// <summary>
/// Encodes a server packet's message into a pooled payload segment (#875): protobuf-net into one stream per thread,
/// rewound for every packet (#640: through a stream protobuf-net allocates nothing for nested messages), then copied
/// into a segment of <see cref="Pool" />. No sealing and no array per packet: the send path seals, when its connection
/// does, as it frames the packet.
/// </summary>
public sealed class PacketEncoder(PayloadSegmentPool pool)
{
    // [ThreadStatic] fields take the t_ prefix, which a naming rule cannot select (the static-field rule asks for s_).
#pragma warning disable IDE1006
    [ThreadStatic] private static MemoryStream? t_stream;
#pragma warning restore IDE1006

    private static readonly MethodInfo s_writeSample = typeof(PacketEncoder)
        .GetMethod(nameof(WriteSample), BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Could not reflect {nameof(PacketEncoder)}.{nameof(WriteSample)}.");

    /// <summary>The encoder every server packet uses, over <see cref="PayloadSegmentPool.Shared" />.</summary>
    public static PacketEncoder Shared { get; } = new(PayloadSegmentPool.Shared);

    public PayloadSegmentPool Pool { get; } = pool;

    public OutboundPacket Encode<T>(T message, NetworkPacketType type, NetworkPacketFlags flags, NetworkProtocol protocol)
        where T : class
    {
        MemoryStream stream = t_stream ??= new MemoryStream(512);
        stream.SetLength(0);
        Write(message, stream);
        return FromPayload(new ReadOnlySpan<byte>(stream.GetBuffer(), 0, (int)stream.Length), type, flags, protocol);
    }

    /// <summary>
    /// A packet whose message is already encoded: <paramref name="payload" />, copied into a segment of <see cref="Pool" />.
    /// For a payload put together from parts encoded once and shared by several packets (#980); the bytes must be the
    /// message's encoding, as <see cref="Encode{T}" /> would write it.
    /// </summary>
    public OutboundPacket FromPayload(ReadOnlySpan<byte> payload, NetworkPacketType type, NetworkPacketFlags flags,
        NetworkProtocol protocol)
    {
        PayloadSegment segment = Pool.Rent(payload);
        return new OutboundPacket(
            new NetworkPacketHeader { Type = type, Flags = flags, Protocol = protocol, Version = 0 }, segment);
    }

    /// <summary>
    /// Writes <paramref name="message" /> at <paramref name="destination" />'s position, byte for byte as
    /// <see cref="Encode{T}" /> writes it into a payload, and lets go of a scratch message's members as it does.
    /// </summary>
    public static void Write<T>(T message, Stream destination) where T : class
    {
        try
        {
            Serializer.Serialize(destination, message);
        }
        finally
        {
            // A scratch message lets go of what this packet set (lists, strings, messages) once it is written, or
            // failed to be (#875).
            if (ReferenceEquals(message, ScratchSlot<T>.t_instance))
                ScratchSlot<T>.s_clear!(message);
        }
    }

    /// <summary>
    /// The calling thread's instance of <typeparamref name="T" />, reset to a new one's values (#875). A factory fills it
    /// and encodes it before it returns, so one instance per thread is enough, and a server packet costs no message
    /// object. Every member is reset on every take, and again once <see cref="Encode{T}" /> has written it, so a value
    /// one packet set can neither reach the next nor stay referenced after it.
    /// </summary>
    /// <remarks>
    /// A collection member comes back null rather than the template's own (protobuf-net writes a null repeated field
    /// as it writes an empty one), so a factory assigns collections and one adding to a list it found there fails at
    /// once instead of reaching every later packet. Any other member comes back as the template has it; a nested
    /// message the template creates (<c>Vector3Dto</c>) is the template's own, so a factory assigns it whole.
    /// </remarks>
    public static T Scratch<T>() where T : class, new()
    {
        T? scratch = ScratchSlot<T>.t_instance;
        if (scratch is null)
        {
            scratch = new T();
            ScratchSlot<T>.t_instance = scratch;
        }

        ScratchOf<T>.s_reset(scratch, ScratchOf<T>.s_template);
        return scratch;
    }

    /// <summary>
    /// Prepares every server packet (<see cref="PrepareServerPacket" />): a <see cref="Packet" /> with a public static
    /// method returning an <see cref="OutboundPacket" />. Run once at startup, off the tick and before the port opens,
    /// so that nothing a send needs is built by the first packet of its type, which on a world is on the tick, and a
    /// packet that could never be sent refuses to start.
    /// </summary>
    /// <returns>The number of message types prepared.</returns>
    public static int PrepareServerPackets()
    {
        int prepared = 0;
        using var sink = new MemoryStream(4096);
        foreach (Type type in typeof(Packet).Assembly.GetTypes())
        {
            if (!type.IsSubclassOf(typeof(Packet)) || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) is null
                || !type.GetMethods(BindingFlags.Public | BindingFlags.Static).Any(m => m.ReturnType == typeof(OutboundPacket)))
            {
                continue;
            }

            Prepare(type, sink);
            prepared++;
        }

        return prepared;
    }

    /// <summary>
    /// Prepares one message type to be sent: the reset of its scratch message (#875), and its protobuf serializer
    /// (#883), with those of the messages inside it.
    /// </summary>
    /// <remarks>
    /// protobuf-net builds a type's serializer on its first use, and the runtime compiles it as it first runs: a
    /// millisecond or two for a packet, and up to some thirty for one that nests several messages, which was paid by
    /// the first packet of each type a server sent. Here the type is written once, into a stream of its own, as a
    /// message with something in every member (<see cref="SampleMessage" />), so all of it is built before any packet
    /// is sent. The model's depth limit (<c>PacketReader.MaxDecodeDepth</c>) need not be set first: protobuf-net reads
    /// it as it writes and reads, not as it builds, so a serializer built before it is set is bound by it all the same
    /// (measured: a type built and used at the default depth is refused past the limit once the limit is set).
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The type's scratch cannot be reset, protobuf-net cannot build its contract or that of a message inside it, or
    /// <see cref="SampleMessage" /> cannot make a message of it with every member set (a shape it has no value for,
    /// a message it cannot create, a setter that refuses): a defect of the server, which then does not start. Left to
    /// the first send, a contract that cannot be built fails every send of the type, on the tick.
    /// </exception>
    public static void PrepareServerPacket(Type type)
    {
        if (type is null)
            throw new ArgumentNullException(nameof(type));

        using var sink = new MemoryStream();
        Prepare(type, sink);
    }

    private static void Prepare(Type type, MemoryStream sink)
    {
        try
        {
            RuntimeHelpers.RunClassConstructor(typeof(ScratchOf<>).MakeGenericType(type).TypeHandle);
        }
        catch (TypeInitializationException e) when (e.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
        }

        object sample;
        try
        {
            sample = SampleMessage.Of(type);
        }
        catch (Exception e)
        {
            // Whatever it is, under the packet's name: what a message inside it throws names that message at best.
            Exception cause = e is TargetInvocationException { InnerException: { } inner } ? inner : e;
            throw new InvalidOperationException(
                $"Packet type {type.FullName} cannot be prepared: {cause.Message}", cause);
        }

        try
        {
            sink.SetLength(0);
            s_writeSample.MakeGenericMethod(type).Invoke(null, [sink, sample]);
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(
                $"Packet type {type.FullName} cannot be encoded: protobuf-net could not build its contract, or that of "
                + "a message inside it. A server does not start with a packet it could never send.",
                e is TargetInvocationException { InnerException: { } inner } ? inner : e);
        }
    }

    /// <summary>The write <see cref="Encode{T}" /> makes, so what it needs for a <typeparamref name="T" /> is what is built.</summary>
    private static void WriteSample<T>(MemoryStream sink, object sample) where T : class =>
        Serializer.Serialize(sink, (T)sample);

    /// <summary>The thread's scratch of <typeparamref name="T" />, and its reset, where <see cref="Encode{T}" /> can see them.</summary>
    private static class ScratchSlot<T> where T : class
    {
#pragma warning disable IDE1006
        [ThreadStatic] internal static T? t_instance;
#pragma warning restore IDE1006

        /// <summary>Resets a scratch to the template; set when the type's reset is built.</summary>
        internal static Action<T>? s_clear;
    }

    private static class ScratchOf<T> where T : class, new()
    {
        internal static readonly T s_template;

        internal static readonly Action<T, T> s_reset;

        static ScratchOf()
        {
            s_template = new T();
            s_reset = BuildReset();
            ScratchSlot<T>.s_clear = static message => s_reset(message, s_template);
        }

        /// <summary>
        /// Assigns every instance field, down the hierarchy, the template's value, or null for a collection (a string
        /// and a <c>byte[]</c> are values here: a bytes field is not repeated). A member that cannot be assigned (a
        /// read-only field, a property without a setter) throws: it would keep one packet's value for the next.
        /// </summary>
        private static Action<T, T> BuildReset()
        {
            ParameterExpression target = Expression.Parameter(typeof(T), "target");
            ParameterExpression source = Expression.Parameter(typeof(T), "source");
            var assignments = new List<Expression>();
            const BindingFlags Members =
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

            for (Type? type = typeof(T); type is not null && type != typeof(object); type = type.BaseType)
            {
                foreach (PropertyInfo property in type.GetProperties(Members))
                {
                    if (property.GetIndexParameters().Length == 0 && property.GetSetMethod(true) is null)
                        throw Unresettable(property.Name, "has no setter");
                }

                foreach (FieldInfo field in type.GetFields(Members))
                {
                    if (field.IsInitOnly)
                        throw Unresettable(field.Name, "is read-only");

                    Expression value = IsCollection(field.FieldType)
                        ? Expression.Constant(null, field.FieldType)
                        : Expression.Field(source, field);
                    assignments.Add(Expression.Assign(Expression.Field(target, field), value));
                }
            }

            Expression body = assignments.Count == 0 ? Expression.Empty() : Expression.Block(assignments);
            return Expression.Lambda<Action<T, T>>(body, target, source).Compile();
        }

        private static bool IsCollection(Type type) =>
            type != typeof(string) && type != typeof(byte[]) && typeof(IEnumerable).IsAssignableFrom(type);

        private static InvalidOperationException Unresettable(string member, string why) =>
            new($"{typeof(T).FullName}.{member} {why}, so a scratch message of the type cannot be reset: one packet's "
                + "value would reach the next (#875).");
    }
}
