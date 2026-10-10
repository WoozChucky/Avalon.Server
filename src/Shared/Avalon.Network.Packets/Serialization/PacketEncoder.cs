using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
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

    /// <summary>The encoder every server packet uses, over <see cref="PayloadSegmentPool.Shared" />.</summary>
    public static PacketEncoder Shared { get; } = new(PayloadSegmentPool.Shared);

    public PayloadSegmentPool Pool { get; } = pool;

    public OutboundPacket Encode<T>(T message, NetworkPacketType type, NetworkPacketFlags flags, NetworkProtocol protocol)
        where T : class
    {
        MemoryStream stream = t_stream ??= new MemoryStream(512);
        stream.SetLength(0);
        Serializer.Serialize(stream, message);
        PayloadSegment payload = Pool.Rent(new ReadOnlySpan<byte>(stream.GetBuffer(), 0, (int)stream.Length));
        return new OutboundPacket(
            new NetworkPacketHeader { Type = type, Flags = flags, Protocol = protocol, Version = 0 }, payload);
    }

    /// <summary>
    /// The calling thread's instance of <typeparamref name="T" />, reset to a new one's values (#875). A factory fills it
    /// and encodes it before it returns, so one instance per thread is enough, and a server packet costs no message
    /// object. Every member is reset on every take, so a value one packet set cannot reach the next.
    /// </summary>
    /// <remarks>
    /// The reset copies a template's member values: a collection a new message starts with is the template's own, so a
    /// factory assigns collections and never adds to one it found there.
    /// </remarks>
    public static T Scratch<T>() where T : class, new()
    {
        T? scratch = ScratchOf<T>.t_instance;
        if (scratch is null)
        {
            scratch = new T();
            ScratchOf<T>.t_instance = scratch;
            return scratch;
        }

        ScratchOf<T>.s_reset(scratch, ScratchOf<T>.s_template);
        return scratch;
    }

    private static class ScratchOf<T> where T : class, new()
    {
#pragma warning disable IDE1006
        [ThreadStatic] internal static T? t_instance;
#pragma warning restore IDE1006

        internal static readonly T s_template = new();

        internal static readonly Action<T, T> s_reset = BuildReset();

        private static Action<T, T> BuildReset()
        {
            ParameterExpression target = Expression.Parameter(typeof(T), "target");
            ParameterExpression source = Expression.Parameter(typeof(T), "source");
            var assignments = new List<Expression>();
            const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (PropertyInfo property in typeof(T).GetProperties(Members))
            {
                if (property.GetIndexParameters().Length == 0 && property.GetGetMethod(true) is not null
                    && property.GetSetMethod(true) is not null)
                {
                    assignments.Add(Expression.Assign(
                        Expression.Property(target, property), Expression.Property(source, property)));
                }
            }

            foreach (FieldInfo field in typeof(T).GetFields(Members))
            {
                // An auto-property's backing field is reset through its property above.
                if (!field.IsInitOnly && !field.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
                    assignments.Add(Expression.Assign(Expression.Field(target, field), Expression.Field(source, field)));
            }

            Expression body = assignments.Count == 0 ? Expression.Empty() : Expression.Block(assignments);
            return Expression.Lambda<Action<T, T>>(body, target, source).Compile();
        }
    }
}
